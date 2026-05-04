using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text;
using System.Xml;
using WinNASClient.Database;
using WinNASClient.Services;
using Dapper;

namespace WinNASClient.HttpServer;

public class WebDavMiddleware
{
    private readonly RequestDelegate _next;
    private readonly DbRepository _repo;
    private readonly AuthService _authService;
    private readonly string _basePath = "/webdav";

    public WebDavMiddleware(RequestDelegate next, DbRepository repo, AuthService authService)
    {
        _next = next;
        _repo = repo;
        _authService = authService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(_basePath))
        {
            await _next(context);
            return;
        }

        var webdavConfig = await _repo.QueryFirstOrDefaultAsync<string>(
            "SELECT Value FROM SystemConfigs WHERE Key = 'webdav_enabled'");
        if (webdavConfig?.ToLower() == "false")
        {
            context.Response.StatusCode = 404;
            return;
        }

        context.Response.Headers["MS-Author-Via"] = "DAV";

        if (context.Request.Method == "OPTIONS")
        {
            await HandleOptions(context);
            return;
        }

        var user = await AuthenticateAsync(context);
        var userId = user?.UserId;

        bool isReadOperation = context.Request.Method is "PROPFIND" or "GET" or "HEAD";
        if (user == null && !isReadOperation)
        {
            context.Response.StatusCode = 401;
            context.Response.Headers["WWW-Authenticate"] = "Basic realm=\"WinNAS WebDAV\"";
            return;
        }

        try
        {
            switch (context.Request.Method)
            {
                case "PROPFIND": await HandlePropFind(context, userId); break;
                case "GET": await HandleGet(context, userId); break;
                case "HEAD": await HandleHead(context, userId); break;
                case "PUT": await HandlePut(context, userId!.Value); break;
                case "DELETE": await HandleDelete(context, userId!.Value); break;
                case "MKCOL": await HandleMkCol(context, userId!.Value); break;
                case "COPY": await HandleCopy(context, userId!.Value); break;
                case "MOVE": await HandleMove(context, userId!.Value); break;
                case "LOCK": await HandleLock(context); break;
                case "UNLOCK": await HandleUnlock(context); break;
                case "PROPPATCH": await HandlePropPatch(context); break;
                default:
                    context.Response.StatusCode = 405;
                    context.Response.Headers["Allow"] = "OPTIONS, PROPFIND, PROPPATCH, GET, HEAD, PUT, DELETE, MKCOL, COPY, MOVE, LOCK, UNLOCK";
                    break;
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "WebDAV error for {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = 500;
        }
    }

    private async Task<(int UserId, string Username)?> AuthenticateAsync(HttpContext context)
    {
        var authHeader = context.Request.Headers["Authorization"].ToString();
        if (string.IsNullOrEmpty(authHeader))
            return null;

        if (authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var encoded = authHeader.Substring(6);
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                var parts = decoded.Split(':', 2);
                if (parts.Length != 2) return null;

                var username = parts[0];
                var password = parts[1];

                var user = await _repo.QueryFirstOrDefaultAsync<Models.User>(
                    "SELECT * FROM Users WHERE Username = @Username AND IsEnabled = 1",
                    new { Username = username });

                if (user == null) return null;
                if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)) return null;

                return (user.Id, user.Username);
            }
            catch { return null; }
        }

        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader.Substring(7);
            var session = await _repo.QueryFirstOrDefaultAsync<Models.UserSession>(
                "SELECT * FROM UserSessions WHERE Token = @Token AND ExpiresAt > @Now",
                new { Token = token, Now = DateTime.UtcNow });

            if (session == null) return null;

            var user = await _repo.QueryFirstOrDefaultAsync<Models.User>(
                "SELECT * FROM Users WHERE Id = @Id AND IsEnabled = 1",
                new { Id = session.UserId });

            if (user == null) return null;

            return (user.Id, user.Username);
        }

        return null;
    }

    private async Task<(string? PhysicalPath, Models.SharedDirectory? Dir)> ResolvePathAsync(string webdavPath, int? userId)
    {
        var segments = webdavPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return (null, null);

        var dirName = Uri.UnescapeDataString(segments[0]);
        var dirs = await _repo.QueryAsync<Models.SharedDirectory>(
            "SELECT * FROM SharedDirectories WHERE AllowWebDav = 1");

        Models.SharedDirectory? dir = null;
        foreach (var d in dirs)
        {
            if (d.Name == dirName || d.Id.ToString() == dirName)
            {
                dir = d;
                break;
            }
        }

        if (dir == null) return (null, null);

        if (userId.HasValue)
        {
            var user = await _repo.QueryFirstOrDefaultAsync<Models.User>(
                "SELECT * FROM Users WHERE Id = @Id", new { Id = userId.Value });
            if (user?.Role != "admin")
            {
                var hasAccess = dir.OwnerId == userId.Value
                    || dir.Visibility == "public"
                    || await _repo.QueryFirstOrDefaultAsync<int?>(
                        "SELECT 1 FROM DirectoryPermissions WHERE DirectoryId = @DirId AND UserId = @UserId AND CanRead = 1",
                        new { DirId = dir.Id, UserId = userId.Value }) != null;
                if (!hasAccess) return (null, null);
            }
        }
        else
        {
            if (dir.Visibility != "public") return (null, null);
        }

        var subPath = segments.Length > 1
            ? string.Join("/", segments[1..].Select(s => Uri.UnescapeDataString(s)))
            : "";

        var fullPath = string.IsNullOrEmpty(subPath)
            ? dir.Path
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(dir.Path, subPath.Replace('/', '\\')));

        if (!fullPath.StartsWith(dir.Path))
            return (null, null);

        return (fullPath, dir);
    }

    private async Task<List<Models.SharedDirectory>> GetAccessibleDirectoriesAsync(int? userId)
    {
        var allDirs = (await _repo.QueryAsync<Models.SharedDirectory>(
            "SELECT * FROM SharedDirectories WHERE AllowWebDav = 1")).ToList();

        if (!userId.HasValue)
            return allDirs.Where(d => d.Visibility == "public").ToList();

        var user = await _repo.QueryFirstOrDefaultAsync<Models.User>(
            "SELECT * FROM Users WHERE Id = @Id", new { Id = userId.Value });

        if (user?.Role == "admin")
            return allDirs;

        var owned = allDirs.Where(d => d.OwnerId == userId.Value);
        var permitted = await _repo.QueryAsync<Models.SharedDirectory>(@"
            SELECT sd.* FROM SharedDirectories sd
            INNER JOIN DirectoryPermissions dp ON sd.Id = dp.DirectoryId
            WHERE dp.UserId = @UserId AND dp.CanRead = 1 AND sd.AllowWebDav = 1", new { UserId = userId.Value });
        var publicDirs = allDirs.Where(d => d.Visibility == "public");

        return owned.Union(permitted).Union(publicDirs).DistinctBy(d => d.Id).ToList();
    }

    private Task HandleOptions(HttpContext context)
    {
        context.Response.StatusCode = 200;
        context.Response.Headers["Allow"] = "OPTIONS, PROPFIND, PROPPATCH, GET, HEAD, PUT, DELETE, MKCOL, COPY, MOVE, LOCK, UNLOCK";
        context.Response.Headers["DAV"] = "1, 2";
        context.Response.Headers["MS-Author-Via"] = "DAV";
        context.Response.Headers["Public"] = "OPTIONS, PROPFIND, PROPPATCH, GET, HEAD, PUT, DELETE, MKCOL, COPY, MOVE, LOCK, UNLOCK";
        return Task.CompletedTask;
    }

    private async Task HandlePropFind(HttpContext context, int? userId)
    {
        var relPath = context.Request.Path.Value![_basePath.Length..].TrimEnd('/');
        var depth = context.Request.Headers["Depth"].FirstOrDefault() ?? "1";

        var xmlBuilder = new StringBuilder();
        xmlBuilder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        xmlBuilder.AppendLine("<D:multistatus xmlns:D=\"DAV:\">");

        if (string.IsNullOrEmpty(relPath) || relPath == "/")
        {
            xmlBuilder.AppendLine(BuildResponse(_basePath + "/", true, "WinNAS", DateTime.Now, 0));

            if (depth != "0")
            {
                var dirs = await GetAccessibleDirectoriesAsync(userId);
                foreach (var dir in dirs)
                {
                    if (Directory.Exists(dir.Path))
                    {
                        var dirInfo = new DirectoryInfo(dir.Path);
                        xmlBuilder.AppendLine(BuildResponse(
                            $"{_basePath}/{Uri.EscapeDataString(dir.Name)}/",
                            true, dir.Name, dirInfo.LastWriteTimeUtc, 0));
                    }
                }
            }
        }
        else
        {
            var (physicalPath, dir) = await ResolvePathAsync(relPath, userId);
            if (physicalPath == null || dir == null)
            {
                context.Response.StatusCode = 404;
                return;
            }

            if (Directory.Exists(physicalPath))
            {
                var dirInfo = new DirectoryInfo(physicalPath);
                xmlBuilder.AppendLine(BuildResponse(
                    $"{_basePath}{relPath}/", true, dirInfo.Name, dirInfo.LastWriteTimeUtc, 0));

                if (depth != "0")
                {
                    try
                    {
                        foreach (var subDir in dirInfo.GetDirectories())
                        {
                            xmlBuilder.AppendLine(BuildResponse(
                                $"{_basePath}{relPath}/{Uri.EscapeDataString(subDir.Name)}/",
                                true, subDir.Name, subDir.LastWriteTimeUtc, 0));
                        }

                        foreach (var file in dirInfo.GetFiles())
                        {
                            xmlBuilder.AppendLine(BuildResponse(
                                $"{_basePath}{relPath}/{Uri.EscapeDataString(file.Name)}",
                                false, file.Name, file.LastWriteTimeUtc, file.Length,
                                file.Extension.TrimStart('.').ToLower()));
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                }
            }
            else if (File.Exists(physicalPath))
            {
                var fileInfo = new FileInfo(physicalPath);
                xmlBuilder.AppendLine(BuildResponse(
                    $"{_basePath}{relPath}", false, fileInfo.Name, fileInfo.LastWriteTimeUtc, fileInfo.Length,
                    fileInfo.Extension.TrimStart('.').ToLower()));
            }
            else
            {
                context.Response.StatusCode = 404;
                return;
            }
        }

        xmlBuilder.AppendLine("</D:multistatus>");

        var xmlContent = xmlBuilder.ToString();
        var xmlBytes = Encoding.UTF8.GetBytes(xmlContent);

        context.Response.StatusCode = 207;
        context.Response.ContentType = "application/xml; charset=utf-8";
        context.Response.Headers["Content-Length"] = xmlBytes.Length.ToString();
        context.Response.Headers["DAV"] = "1, 2";
        context.Response.Headers["MS-Author-Via"] = "DAV";
        await context.Response.Body.WriteAsync(xmlBytes);
    }

    private static string BuildResponse(string href, bool isCollection, string name,
        DateTime lastModified, long size, string? contentType = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("  <D:response>");
        sb.AppendLine($"    <D:href>{EscapeXml(href)}</D:href>");
        sb.AppendLine("    <D:propstat>");
        sb.AppendLine("      <D:prop>");
        sb.AppendLine($"        <D:resourcetype>{(isCollection ? "<D:collection/>" : "")}</D:resourcetype>");
        sb.AppendLine($"        <D:displayname>{EscapeXml(name)}</D:displayname>");
        sb.AppendLine($"        <D:getlastmodified>{lastModified:R}</D:getlastmodified>");
        sb.AppendLine($"        <D:creationdate>{lastModified:yyyy-MM-ddTHH:mm:ssZ}</D:creationdate>");
        if (isCollection)
        {
            sb.AppendLine("        <D:getcontenttype>httpd/unix-directory</D:getcontenttype>");
            sb.AppendLine("        <D:supportedlock>");
            sb.AppendLine("          <D:lockentry>");
            sb.AppendLine("            <D:lockscope><D:exclusive/></D:lockscope>");
            sb.AppendLine("            <D:locktype><D:write/></D:locktype>");
            sb.AppendLine("          </D:lockentry>");
            sb.AppendLine("          <D:lockentry>");
            sb.AppendLine("            <D:lockscope><D:shared/></D:lockscope>");
            sb.AppendLine("            <D:locktype><D:write/></D:locktype>");
            sb.AppendLine("          </D:lockentry>");
            sb.AppendLine("        </D:supportedlock>");
            sb.AppendLine("        <D:ishidden>0</D:ishidden>");
        }
        else
        {
            sb.AppendLine($"        <D:getcontentlength>{size}</D:getcontentlength>");
            if (!string.IsNullOrEmpty(contentType))
                sb.AppendLine($"        <D:getcontenttype>{EscapeXml(GetMimeType(contentType))}</D:getcontenttype>");
        }
        sb.AppendLine("      </D:prop>");
        sb.AppendLine("      <D:status>HTTP/1.1 200 OK</D:status>");
        sb.AppendLine("    </D:propstat>");
        sb.AppendLine("  </D:response>");
        return sb.ToString();
    }

    private async Task HandleGet(HttpContext context, int? userId)
    {
        var relPath = context.Request.Path.Value![_basePath.Length..].TrimEnd('/');
        if (string.IsNullOrEmpty(relPath) || relPath == "/")
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            var dirs = await GetAccessibleDirectoriesAsync(userId);
            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'><title>WinNAS WebDAV</title>");
            html.AppendLine("<style>body{font-family:sans-serif;margin:40px}h1{color:#333}ul{list-style:none;padding:0}li{padding:8px 0;border-bottom:1px solid #eee}a{color:#409eff;text-decoration:none}a:hover{text-decoration:underline}</style>");
            html.AppendLine("</head><body><h1>WinNAS WebDAV</h1><ul>");
            foreach (var d in dirs)
            {
                if (Directory.Exists(d.Path))
                    html.AppendLine($"<li>📁 <a href=\"{EscapeHtml(_basePath)}/{Uri.EscapeDataString(d.Name)}/\">{EscapeHtml(d.Name)}</a></li>");
            }
            html.AppendLine("</ul></body></html>");
            await context.Response.WriteAsync(html.ToString());
            return;
        }

        var (physicalPath, dir) = await ResolvePathAsync(relPath, userId);
        if (physicalPath == null || dir == null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        if (Directory.Exists(physicalPath))
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            var dirInfo = new DirectoryInfo(physicalPath);
            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'><title>WinNAS WebDAV</title>");
            html.AppendLine("<style>body{font-family:sans-serif;margin:40px}h1{color:#333}ul{list-style:none;padding:0}li{padding:8px 0;border-bottom:1px solid #eee}a{color:#409eff;text-decoration:none}a:hover{text-decoration:underline}</style>");
            html.AppendLine($"</head><body><h1>{EscapeHtml(dirInfo.Name)}</h1><ul>");
            try
            {
                foreach (var subDir in dirInfo.GetDirectories())
                    html.AppendLine($"<li>📁 <a href=\"{Uri.EscapeDataString(subDir.Name)}/\">{EscapeHtml(subDir.Name)}</a></li>");
                foreach (var file in dirInfo.GetFiles())
                    html.AppendLine($"<li>📄 <a href=\"{Uri.EscapeDataString(file.Name)}\">{EscapeHtml(file.Name)}</a> ({FormatSize(file.Length)})</li>");
            }
            catch (UnauthorizedAccessException) { }
            html.AppendLine("</ul></body></html>");
            await context.Response.WriteAsync(html.ToString());
            return;
        }

        if (File.Exists(physicalPath))
        {
            var fileInfo = new FileInfo(physicalPath);
            var mimeType = GetMimeType(fileInfo.Extension.TrimStart('.').ToLower());
            context.Response.ContentType = mimeType;
            context.Response.Headers["Content-Disposition"] = $"inline; filename=\"{Uri.EscapeDataString(fileInfo.Name)}\"";
            await SendFileWithRangeSupport(context, physicalPath);
            return;
        }

        context.Response.StatusCode = 404;
    }

    private async Task HandleHead(HttpContext context, int? userId)
    {
        var relPath = context.Request.Path.Value![_basePath.Length..].TrimEnd('/');
        if (string.IsNullOrEmpty(relPath) || relPath == "/")
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            return;
        }

        var (physicalPath, dir) = await ResolvePathAsync(relPath, userId);
        if (physicalPath == null || dir == null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        if (Directory.Exists(physicalPath))
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            return;
        }

        if (File.Exists(physicalPath))
        {
            var fileInfo = new FileInfo(physicalPath);
            var mimeType = GetMimeType(fileInfo.Extension.TrimStart('.').ToLower());
            context.Response.StatusCode = 200;
            context.Response.ContentType = mimeType;
            context.Response.Headers["Content-Length"] = fileInfo.Length.ToString();
            return;
        }

        context.Response.StatusCode = 404;
    }

    private async Task HandlePut(HttpContext context, int userId)
    {
        var relPath = context.Request.Path.Value![_basePath.Length..].TrimEnd('/');
        var (physicalPath, dir) = await ResolvePathAsync(relPath, userId);
        if (physicalPath == null || dir == null)
        {
            context.Response.StatusCode = 409;
            return;
        }

        var parentDir = System.IO.Path.GetDirectoryName(physicalPath);
        if (parentDir != null && !Directory.Exists(parentDir))
        {
            context.Response.StatusCode = 409;
            return;
        }

        var isNew = !File.Exists(physicalPath);

        await using (var fs = File.Create(physicalPath))
        {
            await context.Request.Body.CopyToAsync(fs);
        }

        context.Response.StatusCode = isNew ? 201 : 204;
    }

    private async Task HandleDelete(HttpContext context, int userId)
    {
        var relPath = context.Request.Path.Value![_basePath.Length..].TrimEnd('/');
        var (physicalPath, dir) = await ResolvePathAsync(relPath, userId);
        if (physicalPath == null || dir == null || !physicalPath.StartsWith(dir.Path))
        {
            context.Response.StatusCode = 404;
            return;
        }

        if (Directory.Exists(physicalPath))
        {
            try
            {
                Directory.Delete(physicalPath, true);
                context.Response.StatusCode = 204;
            }
            catch
            {
                context.Response.StatusCode = 403;
            }
        }
        else if (File.Exists(physicalPath))
        {
            try
            {
                File.Delete(physicalPath);
                context.Response.StatusCode = 204;
            }
            catch
            {
                context.Response.StatusCode = 403;
            }
        }
        else
        {
            context.Response.StatusCode = 404;
        }
    }

    private async Task HandleMkCol(HttpContext context, int userId)
    {
        var relPath = context.Request.Path.Value![_basePath.Length..].TrimEnd('/');
        var (physicalPath, dir) = await ResolvePathAsync(relPath, userId);
        if (physicalPath == null || dir == null)
        {
            context.Response.StatusCode = 409;
            return;
        }

        if (Directory.Exists(physicalPath))
        {
            context.Response.StatusCode = 405;
            return;
        }

        if (File.Exists(physicalPath))
        {
            context.Response.StatusCode = 409;
            return;
        }

        try
        {
            Directory.CreateDirectory(physicalPath);
            context.Response.StatusCode = 201;
        }
        catch
        {
            context.Response.StatusCode = 403;
        }
    }

    private async Task HandleCopy(HttpContext context, int userId)
    {
        var srcRelPath = context.Request.Path.Value![_basePath.Length..].TrimEnd('/');
        var (srcPath, srcDir) = await ResolvePathAsync(srcRelPath, userId);
        if (srcPath == null || srcDir == null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        var destUri = context.Request.Headers["Destination"].ToString();
        if (string.IsNullOrEmpty(destUri))
        {
            context.Response.StatusCode = 400;
            return;
        }

        var destRelPath = ExtractPathFromUri(destUri);
        var (destPath, destDir) = await ResolvePathAsync(destRelPath, userId);
        if (destPath == null || destDir == null)
        {
            context.Response.StatusCode = 409;
            return;
        }

        try
        {
            if (Directory.Exists(srcPath))
            {
                CopyDirectoryRecursive(srcPath, destPath);
            }
            else if (File.Exists(srcPath))
            {
                var destDir2 = System.IO.Path.GetDirectoryName(destPath);
                if (destDir2 != null && !Directory.Exists(destDir2))
                    Directory.CreateDirectory(destDir2);
                File.Copy(srcPath, destPath, true);
            }
            context.Response.StatusCode = 201;
        }
        catch
        {
            context.Response.StatusCode = 403;
        }
    }

    private async Task HandleMove(HttpContext context, int userId)
    {
        var srcRelPath = context.Request.Path.Value![_basePath.Length..].TrimEnd('/');
        var (srcPath, srcDir) = await ResolvePathAsync(srcRelPath, userId);
        if (srcPath == null || srcDir == null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        var destUri = context.Request.Headers["Destination"].ToString();
        if (string.IsNullOrEmpty(destUri))
        {
            context.Response.StatusCode = 400;
            return;
        }

        var destRelPath = ExtractPathFromUri(destUri);
        var (destPath, destDir) = await ResolvePathAsync(destRelPath, userId);
        if (destPath == null || destDir == null)
        {
            context.Response.StatusCode = 409;
            return;
        }

        try
        {
            if (Directory.Exists(srcPath))
            {
                if (Directory.Exists(destPath))
                    Directory.Delete(destPath, true);
                Directory.Move(srcPath, destPath);
            }
            else if (File.Exists(srcPath))
            {
                var destDir2 = System.IO.Path.GetDirectoryName(destPath);
                if (destDir2 != null && !Directory.Exists(destDir2))
                    Directory.CreateDirectory(destDir2);
                try
                {
                    File.Move(srcPath, destPath);
                }
                catch (Exception)
                {
                    File.Copy(srcPath, destPath, true);
                    File.Delete(srcPath);
                }
            }
            context.Response.StatusCode = 201;
        }
        catch
        {
            context.Response.StatusCode = 403;
        }
    }

    private Task HandleLock(HttpContext context)
    {
        var lockToken = $"urn:uuid:{Guid.NewGuid()}";

        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<D:prop xmlns:D=""DAV:"">
  <D:lockdiscovery>
    <D:activelock>
      <D:locktype><D:write/></D:locktype>
      <D:lockscope><D:exclusive/></D:lockscope>
      <D:locktoken><D:href>" + lockToken + @"</D:href></D:locktoken>
      <D:depth>infinity</D:depth>
      <D:timeout>Second-3600</D:timeout>
    </D:activelock>
  </D:lockdiscovery>
</D:prop>";

        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/xml; charset=utf-8";
        context.Response.Headers["Lock-Token"] = $"<{lockToken}>";
        return context.Response.WriteAsync(xml);
    }

    private Task HandleUnlock(HttpContext context)
    {
        context.Response.StatusCode = 204;
        return Task.CompletedTask;
    }

    private Task HandlePropPatch(HttpContext context)
    {
        context.Response.StatusCode = 207;
        context.Response.ContentType = "application/xml; charset=utf-8";
        context.Response.Headers["DAV"] = "1, 2";
        return context.Response.WriteAsync("<?xml version=\"1.0\" encoding=\"utf-8\"?><D:multistatus xmlns:D=\"DAV:\"><D:response><D:href>" + EscapeXml(context.Request.Path) + "</D:href><D:propstat><D:prop/><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response></D:multistatus>");
    }

    private string ExtractPathFromUri(string uri)
    {
        try
        {
            if (Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out var result))
            {
                if (result.IsAbsoluteUri)
                    return result.AbsolutePath;
                return result.OriginalString;
            }
        }
        catch { }

        if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var idx = uri.IndexOf('/', uri.IndexOf("//", StringComparison.OrdinalIgnoreCase) + 2);
            if (idx >= 0) return uri[idx..];
        }

        return uri;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        if (!Directory.Exists(destDir))
            Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(file)), true);

        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectoryRecursive(dir, System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(dir)));
    }

    private static string EscapeXml(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
    }

    private static string EscapeHtml(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return $"{size:0.##} {units[unitIndex]}";
    }

    private static async Task SendFileWithRangeSupport(HttpContext context, string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var fileSize = fileInfo.Length;
        var rangeHeader = context.Request.Headers["Range"].ToString();

        context.Response.Headers["Accept-Ranges"] = "bytes";

        if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes="))
        {
            var rangeParts = rangeHeader.Substring(6).Split('-');
            if (long.TryParse(rangeParts[0], out var start))
            {
                var end = rangeParts.Length > 1 && long.TryParse(rangeParts[1], out var e) ? e : fileSize - 1;
                end = Math.Min(end, fileSize - 1);

                if (start > end || start >= fileSize)
                {
                    context.Response.StatusCode = 416;
                    context.Response.Headers["Content-Range"] = $"bytes */{fileSize}";
                    return;
                }

                context.Response.StatusCode = 206;
                context.Response.Headers["Content-Range"] = $"bytes {start}-{end}/{fileSize}";
                context.Response.Headers["Content-Length"] = (end - start + 1).ToString();

                await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Seek(start, SeekOrigin.Begin);
                var buffer = new byte[81920];
                var remaining = end - start + 1;
                while (remaining > 0)
                {
                    var toRead = (int)Math.Min(buffer.Length, remaining);
                    var read = await fs.ReadAsync(buffer, 0, toRead);
                    if (read == 0) break;
                    await context.Response.Body.WriteAsync(buffer, 0, read);
                    remaining -= read;
                }
                return;
            }
        }

        context.Response.StatusCode = 200;
        context.Response.Headers["Content-Length"] = fileSize.ToString();
        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buf = new byte[81920];
        int bytesRead;
        while ((bytesRead = await fileStream.ReadAsync(buf, 0, buf.Length)) > 0)
        {
            await context.Response.Body.WriteAsync(buf, 0, bytesRead);
        }
    }

    private static string GetMimeType(string ext)
    {
        return ext.ToLower() switch
        {
            "txt" or "log" or "md" => "text/plain",
            "html" or "htm" => "text/html",
            "css" => "text/css",
            "js" => "application/javascript",
            "json" => "application/json",
            "xml" => "application/xml",
            "pdf" => "application/pdf",
            "zip" => "application/zip",
            "rar" or "7z" => "application/x-compressed",
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            "webp" => "image/webp",
            "svg" => "image/svg+xml",
            "ico" => "image/x-icon",
            "mp4" => "video/mp4",
            "webm" => "video/webm",
            "avi" => "video/x-msvideo",
            "mkv" => "video/x-matroska",
            "mp3" => "audio/mpeg",
            "wav" => "audio/wav",
            "flac" => "audio/flac",
            "ogg" => "audio/ogg",
            "doc" => "application/msword",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "xls" => "application/vnd.ms-excel",
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "ppt" => "application/vnd.ms-powerpoint",
            "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            _ => "application/octet-stream"
        };
    }
}
