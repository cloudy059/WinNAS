using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using WinNASClient.Config;
using WinNASClient.Services;
using WinNASClient.Utils;

using WinNASClient.Database;
using System.IO.Compression;

namespace WinNASClient.HttpServer.Controllers;

public class FileController
{
    private readonly FileService _fileService;
    private readonly ShareService _shareService;
    private readonly SystemService _systemService;
    private readonly AuthService _authService;
    private readonly AppConfig _config;
    private readonly DbRepository _repo;

    public FileController(FileService fileService, ShareService shareService, SystemService systemService, AuthService authService, AppConfig config, DbRepository repo)
    {
        _fileService = fileService;
        _shareService = shareService;
        _systemService = systemService;
        _authService = authService;
        _config = config;
        _repo = repo;
    }

    public void Register(WebApplication app)
    {
        app.MapGet("/api/files/directories", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var dirs = await _fileService.GetSharedDirectoriesAsync(userId);
            return Results.Ok(new { success = true, data = dirs });
        }).RequireAuthorization();

        app.MapPost("/api/files/directories", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null || user.Role != "admin")
                return Results.Ok(new { success = false, message = "仅管理员可添加共享目录" });

            var form = await ctx.Request.ReadFromJsonAsync<CreateDirectoryRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var dir = new Models.SharedDirectory
            {
                Name = form.Name,
                Path = form.Path,
                Description = form.Description ?? "",
                OwnerId = userId,
                Visibility = form.Visibility ?? "private",
                AllowUpload = form.AllowUpload ?? true,
                AllowDelete = form.AllowDelete ?? false,
                AllowRename = form.AllowRename ?? false,
                AllowMove = form.AllowMove ?? false
            };

            var created = await _fileService.CreateDirectoryAsync(dir);
            await _systemService.LogOperationAsync(userId, "", "create_directory", dir.Name, "", ctx.Connection.RemoteIpAddress?.ToString() ?? "");

            return Results.Ok(new { success = true, data = created });
        }).RequireAuthorization();

        app.MapPut("/api/files/directories/{id}", async (int id, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var dir = await _fileService.GetDirectoryAsync(id);
            if (dir == null) return Results.Ok(new { success = false, message = "目录不存在" });
            if (dir.OwnerId != userId) return Results.Ok(new { success = false, message = "没有权限" });

            var form = await ctx.Request.ReadFromJsonAsync<UpdateDirectoryRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            dir.Name = form.Name ?? dir.Name;
            dir.Description = form.Description ?? dir.Description;
            dir.Visibility = form.Visibility ?? dir.Visibility;
            dir.AllowUpload = form.AllowUpload ?? dir.AllowUpload;
            dir.AllowDelete = form.AllowDelete ?? dir.AllowDelete;
            dir.AllowRename = form.AllowRename ?? dir.AllowRename;
            dir.AllowMove = form.AllowMove ?? dir.AllowMove;

            await _fileService.UpdateDirectoryAsync(dir);
            return Results.Ok(new { success = true, message = "更新成功" });
        }).RequireAuthorization();

        app.MapDelete("/api/files/directories/{id}", async (int id, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var dir = await _fileService.GetDirectoryAsync(id);
            if (dir == null) return Results.Ok(new { success = false, message = "目录不存在" });
            if (dir.OwnerId != userId) return Results.Ok(new { success = false, message = "没有权限" });

            await _fileService.DeleteDirectoryAsync(id);
            return Results.Ok(new { success = true, message = "删除成功" });
        }).RequireAuthorization();

        app.MapGet("/api/files/list", async (int dirId, string? path, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var hasPerm = await _fileService.HasPermissionAsync(dirId, userId, "CanRead");
            if (!hasPerm) return Results.Ok(new { success = false, message = "没有权限" });

            var dir = await _fileService.GetDirectoryAsync(dirId);
            if (dir == null) return Results.Ok(new { success = false, message = "目录不存在" });

            var files = _fileService.ListFiles(dir.Path, path);
            return Results.Ok(new { success = true, data = files, dirPath = dir.Path });
        }).RequireAuthorization();

        app.MapPost("/api/files/upload", async (int dirId, string? path, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var hasPerm = await _fileService.HasPermissionAsync(dirId, userId, "CanWrite");
            if (!hasPerm) return Results.Ok(new { success = false, message = "没有权限" });

            var dir = await _fileService.GetDirectoryAsync(dirId);
            if (dir == null) return Results.Ok(new { success = false, message = "目录不存在" });

            var targetPath = string.IsNullOrEmpty(path) ? dir.Path : Path.Combine(dir.Path, path);
            if (!Directory.Exists(targetPath))
                Directory.CreateDirectory(targetPath);

            var results = new List<object>();
            var form = await ctx.Request.ReadFormAsync();

            foreach (var file in form.Files)
            {
                if (file.Length > _config.MaxUploadSize)
                {
                    results.Add(new { file.FileName, success = false, message = "文件超过大小限制" });
                    continue;
                }

                var filePath = Path.Combine(targetPath, file.FileName);
                var fileDir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir))
                    Directory.CreateDirectory(fileDir);

                var fileNameOnly = Path.GetFileName(file.FileName);
                var (isDuplicate, existingPath) = await _fileService.CheckDuplicateAsync(fileDir ?? targetPath, fileNameOnly);
                if (isDuplicate)
                {
                    var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileNameOnly);
                    var ext = Path.GetExtension(fileNameOnly);
                    var newFileName = $"{fileNameWithoutExt}_{DateTime.Now:yyyyMMddHHmmss}{ext}";
                    filePath = Path.Combine(fileDir ?? targetPath, newFileName);
                }

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var fileInfo = new FileInfo(filePath);
                await _fileService.RecordFileAsync(new Models.FileRecord
                {
                    Name = fileInfo.Name,
                    Path = fileInfo.FullName,
                    DirectoryPath = targetPath,
                    DirectoryId = dirId,
                    Size = fileInfo.Length,
                    Extension = fileInfo.Extension.ToLowerInvariant(),
                    MimeType = GetMimeType(fileInfo.Extension),
                    Md5 = CryptoHelper.ComputeMd5(filePath),
                    OwnerId = userId
                });

                results.Add(new { fileName = fileInfo.Name, success = true });
            }

            await _systemService.LogOperationAsync(userId, "", "upload", dir.Name, $"上传了{results.Count}个文件", ctx.Connection.RemoteIpAddress?.ToString() ?? "");

            return Results.Ok(new { success = true, data = results });
        }).RequireAuthorization();

        app.MapGet("/api/files/download", async (int dirId, string path, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var hasPerm = await _fileService.HasPermissionAsync(dirId, userId, "CanRead");
            if (!hasPerm) return Results.Ok(new { success = false, message = "没有权限" });

            var dir = await _fileService.GetDirectoryAsync(dirId);
            if (dir == null) return Results.Ok(new { success = false, message = "目录不存在" });

            var filePath = Path.GetFullPath(Path.Combine(dir.Path, path));
            if (!filePath.StartsWith(Path.GetFullPath(dir.Path), StringComparison.OrdinalIgnoreCase))
                return Results.Ok(new { success = false, message = "非法路径" });

            if (File.Exists(filePath))
            {
                await _systemService.LogOperationAsync(userId, "", "download", path, "", ctx.Connection.RemoteIpAddress?.ToString() ?? "");
                return SendFileWithRangeSupport(ctx, filePath);
            }

            if (Directory.Exists(filePath))
            {
                await _systemService.LogOperationAsync(userId, "", "download", path, "", ctx.Connection.RemoteIpAddress?.ToString() ?? "");
                var dirName = new DirectoryInfo(filePath).Name;
                var ms = new MemoryStream();
                using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
                {
                    foreach (var file in Directory.GetFiles(filePath, "*", SearchOption.AllDirectories))
                    {
                        var entryName = Path.GetRelativePath(filePath, file);
                        archive.CreateEntryFromFile(file, entryName);
                    }
                }
                ms.Position = 0;
                return Results.File(ms, "application/zip", $"{dirName}.zip");
            }

            return Results.Ok(new { success = false, message = "文件不存在" });
        }).RequireAuthorization();

        app.MapGet("/api/files/stream", async (int dirId, string path, string? token, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == 0 && !string.IsNullOrEmpty(token))
            {
                var principal = Utils.JwtHelper.ValidateToken(token.Replace("Bearer ", ""), _config.JwtSecret);
                if (principal != null)
                {
                    userId = int.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                }
            }

            if (userId == 0) return Results.Unauthorized();

            var hasPerm = await _fileService.HasPermissionAsync(dirId, userId, "CanRead");
            if (!hasPerm) return Results.Ok(new { success = false, message = "没有权限" });

            var dir = await _fileService.GetDirectoryAsync(dirId);
            if (dir == null) return Results.Ok(new { success = false, message = "目录不存在" });

            var filePath = Path.GetFullPath(Path.Combine(dir.Path, path));
            if (!filePath.StartsWith(Path.GetFullPath(dir.Path), StringComparison.OrdinalIgnoreCase))
                return Results.Ok(new { success = false, message = "非法路径" });

            if (!File.Exists(filePath))
                return Results.Ok(new { success = false, message = "文件不存在" });

            return SendFileWithRangeSupport(ctx, filePath);
        });

        app.MapPut("/api/files/rename", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<RenameFileRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var hasPerm = await _fileService.HasPermissionAsync(form.DirectoryId, userId, "CanRename");
            if (!hasPerm) return Results.Ok(new { success = false, message = "没有重命名权限" });

            if (form.RecordId > 0)
            {
                var success = await _fileService.RenameFileAsync(form.RecordId, form.NewName);
                return Results.Ok(new { success, message = success ? "重命名成功" : "重命名失败" });
            }

            if (string.IsNullOrWhiteSpace(form.RelativePath))
                return Results.Ok(new { success = false, message = "缺少文件路径" });

            var dir = await _fileService.GetDirectoryAsync(form.DirectoryId);
            if (dir == null) return Results.Ok(new { success = false, message = "目录不存在" });

            var fullPath = Path.GetFullPath(Path.Combine(dir.Path, form.RelativePath));
            if (!fullPath.StartsWith(Path.GetFullPath(dir.Path), StringComparison.OrdinalIgnoreCase))
                return Results.Ok(new { success = false, message = "非法路径" });

            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                return Results.Ok(new { success = false, message = "文件或目录不存在" });

            var newPath = Path.Combine(Path.GetDirectoryName(fullPath)!, form.NewName);

            try
            {
                if (File.Exists(fullPath))
                    File.Move(fullPath, newPath);
                else if (Directory.Exists(fullPath))
                    Directory.Move(fullPath, newPath);

                await _repo.ExecuteAsync("UPDATE FileRecords SET Name = @Name, Path = @NewPath WHERE Path = @OldPath",
                    new { Name = form.NewName, NewPath = newPath, OldPath = fullPath });

                return Results.Ok(new { success = true, message = "重命名成功" });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = $"重命名失败: {ex.Message}" });
            }
        }).RequireAuthorization();

        app.MapDelete("/api/files/delete", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<DeleteFileRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var hasPerm = await _fileService.HasPermissionAsync(form.DirectoryId, userId, "CanDelete");
            if (!hasPerm) return Results.Ok(new { success = false, message = "没有删除权限" });

            if (form.RecordId > 0)
            {
                var success = await _fileService.DeleteFileAsync(form.RecordId);
                return Results.Ok(new { success, message = success ? "删除成功" : "删除失败" });
            }

            if (string.IsNullOrWhiteSpace(form.RelativePath))
                return Results.Ok(new { success = false, message = "缺少文件路径" });

            var dir = await _fileService.GetDirectoryAsync(form.DirectoryId);
            if (dir == null) return Results.Ok(new { success = false, message = "目录不存在" });

            var fullPath = Path.GetFullPath(Path.Combine(dir.Path, form.RelativePath));
            if (!fullPath.StartsWith(Path.GetFullPath(dir.Path), StringComparison.OrdinalIgnoreCase))
                return Results.Ok(new { success = false, message = "非法路径" });

            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                return Results.Ok(new { success = false, message = "文件或目录不存在" });

            try
            {
                var recycledDir = Path.Combine(Path.GetTempPath(), "WinNAS_Recycle", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(recycledDir);

                if (File.Exists(fullPath))
                {
                    var fileSize = new FileInfo(fullPath).Length;
                    var recycledPath = Path.Combine(recycledDir, Path.GetFileName(fullPath));
                    File.Copy(fullPath, recycledPath, true);
                    File.Delete(fullPath);
                    await _repo.ExecuteAsync(
                        "INSERT INTO RecycleBin (DirectoryId, FileName, OriginalPath, RecycledPath, IsDirectory, Size, UserId, Username) VALUES (@DirId, @Name, @OrigPath, @RecycPath, 0, @Size, @UserId, @Username)",
                        new { DirId = form.DirectoryId, Name = Path.GetFileName(fullPath), OrigPath = fullPath, RecycPath = recycledPath, Size = fileSize, UserId = userId, Username = "" });
                }
                else if (Directory.Exists(fullPath))
                {
                    var recycledPath = Path.Combine(recycledDir, Path.GetFileName(fullPath));
                    CopyDirectory(fullPath, recycledPath);
                    Directory.Delete(fullPath, true);
                    await _repo.ExecuteAsync(
                        "INSERT INTO RecycleBin (DirectoryId, FileName, OriginalPath, RecycledPath, IsDirectory, Size, UserId, Username) VALUES (@DirId, @Name, @OrigPath, @RecycPath, 1, 0, @UserId, @Username)",
                        new { DirId = form.DirectoryId, Name = Path.GetFileName(fullPath), OrigPath = fullPath, RecycPath = recycledPath, UserId = userId, Username = "" });
                }

                await _repo.ExecuteAsync("DELETE FROM FileRecords WHERE Path = @Path", new { Path = fullPath });

                return Results.Ok(new { success = true, message = "已移至回收站" });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = $"删除失败: {ex.Message}" });
            }
        }).RequireAuthorization();

        app.MapPut("/api/files/move", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<MoveFileRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var hasPerm = await _fileService.HasPermissionAsync(form.DirectoryId, userId, "CanMove");
            if (!hasPerm) return Results.Ok(new { success = false, message = "没有移动权限" });

            var sourceDir = await _fileService.GetDirectoryAsync(form.DirectoryId);
            if (sourceDir == null) return Results.Ok(new { success = false, message = "源目录不存在" });

            var targetDir = await _fileService.GetDirectoryAsync(form.TargetDirectoryId);
            if (targetDir == null) return Results.Ok(new { success = false, message = "目标目录不存在" });

            var targetPath = string.IsNullOrEmpty(form.TargetSubPath)
                ? targetDir.Path
                : Path.Combine(targetDir.Path, form.TargetSubPath);

            if (!Directory.Exists(targetPath))
                Directory.CreateDirectory(targetPath);

            try
            {
                if (form.RecordId > 0)
                {
                    var success = await _fileService.MoveFileAsync(form.RecordId, targetPath);
                    if (success)
                        await _systemService.LogOperationAsync(userId, "", "move", form.RecordId.ToString(), $"移动到{targetPath}", ctx.Connection.RemoteIpAddress?.ToString() ?? "");
                    return Results.Ok(new { success, message = success ? "移动成功" : "移动失败" });
                }

                if (!string.IsNullOrWhiteSpace(form.SourceRelativePath))
                {
                    var sourcePath = Path.GetFullPath(Path.Combine(sourceDir.Path, form.SourceRelativePath));
                    if (!sourcePath.StartsWith(Path.GetFullPath(sourceDir.Path), StringComparison.OrdinalIgnoreCase))
                        return Results.Ok(new { success = false, message = "非法路径" });

                    var fileName = Path.GetFileName(sourcePath);
                    var destPath = Path.Combine(targetPath, fileName);

                    if (sourcePath.Equals(destPath, StringComparison.OrdinalIgnoreCase))
                        return Results.Ok(new { success = false, message = "源文件和目标位置相同" });

                    try
                    {
                        if (File.Exists(sourcePath))
                            File.Move(sourcePath, destPath, true);
                        else if (Directory.Exists(sourcePath))
                            Directory.Move(sourcePath, destPath);
                        else
                            return Results.Ok(new { success = false, message = "源文件不存在" });
                    }
                    catch (IOException)
                    {
                        if (File.Exists(sourcePath))
                        { File.Copy(sourcePath, destPath, true); File.Delete(sourcePath); }
                        else if (Directory.Exists(sourcePath))
                        { CopyDirectory(sourcePath, destPath); Directory.Delete(sourcePath, true); }
                    }

                    await _systemService.LogOperationAsync(userId, "", "move", form.SourceRelativePath, $"移动到{targetPath}", ctx.Connection.RemoteIpAddress?.ToString() ?? "");
                    return Results.Ok(new { success = true, message = "移动成功" });
                }

                if (!string.IsNullOrWhiteSpace(form.SourceFileName))
                {
                    var sourcePath = Path.GetFullPath(Path.Combine(sourceDir.Path, form.SourceFileName));
                    if (!sourcePath.StartsWith(Path.GetFullPath(sourceDir.Path), StringComparison.OrdinalIgnoreCase))
                        return Results.Ok(new { success = false, message = "非法路径" });

                    var destPath = Path.Combine(targetPath, form.SourceFileName);

                    if (sourcePath.Equals(destPath, StringComparison.OrdinalIgnoreCase))
                        return Results.Ok(new { success = false, message = "源文件和目标位置相同" });

                    try
                    {
                        if (File.Exists(sourcePath))
                            File.Move(sourcePath, destPath, true);
                        else if (Directory.Exists(sourcePath))
                            Directory.Move(sourcePath, destPath);
                        else
                            return Results.Ok(new { success = false, message = "源文件不存在" });
                    }
                    catch (IOException)
                    {
                        if (File.Exists(sourcePath))
                        { File.Copy(sourcePath, destPath, true); File.Delete(sourcePath); }
                        else if (Directory.Exists(sourcePath))
                        { CopyDirectory(sourcePath, destPath); Directory.Delete(sourcePath, true); }
                    }

                    await _systemService.LogOperationAsync(userId, "", "move", form.SourceFileName, $"移动到{targetPath}", ctx.Connection.RemoteIpAddress?.ToString() ?? "");
                    return Results.Ok(new { success = true, message = "移动成功" });
                }

                return Results.Ok(new { success = false, message = "缺少文件标识" });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = $"移动失败: {ex.Message}" });
            }
        }).RequireAuthorization();

        app.MapPost("/api/files/share", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<ShareFileRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var dir = await _fileService.GetDirectoryAsync(form.DirectoryId);
            if (dir == null) return Results.Ok(new { success = false, message = "目录不存在" });

            string sharePath;
            if (string.IsNullOrWhiteSpace(form.FilePath))
            {
                sharePath = dir.Path;
            }
            else
            {
                sharePath = Path.GetFullPath(Path.Combine(dir.Path, form.FilePath));
                if (!sharePath.StartsWith(Path.GetFullPath(dir.Path), StringComparison.OrdinalIgnoreCase))
                    return Results.Ok(new { success = false, message = "非法路径" });
            }

            if (!File.Exists(sharePath) && !Directory.Exists(sharePath))
                return Results.Ok(new { success = false, message = "文件或目录不存在" });

            var link = await _shareService.CreateShareLinkAsync(
                form.DirectoryId, sharePath, userId,
                form.Password, form.ExpiresAt, form.MaxDownloads);

            await _systemService.LogOperationAsync(userId, "", "share_file", form.FilePath ?? dir.Path, link.Code, ctx.Connection.RemoteIpAddress?.ToString() ?? "");

            return Results.Ok(new { success = true, data = link });
        }).RequireAuthorization();

        app.MapPost("/api/files/permissions", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<SetPermissionRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var dir = await _fileService.GetDirectoryAsync(form.DirectoryId);
            if (dir == null || dir.OwnerId != userId) return Results.Ok(new { success = false, message = "没有权限" });

            await _fileService.SetPermissionAsync(new Models.DirectoryPermission
            {
                DirectoryId = form.DirectoryId,
                UserId = form.UserId,
                CanRead = form.CanRead,
                CanWrite = form.CanWrite,
                CanDelete = form.CanDelete,
                CanRename = form.CanRename,
                CanMove = form.CanMove
            });

            return Results.Ok(new { success = true, message = "权限设置成功" });
        }).RequireAuthorization();

        app.MapPost("/api/files/folder", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<CreateFolderRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var hasPerm = await _fileService.HasPermissionAsync(form.DirId, userId, "CanWrite");
            if (!hasPerm) return Results.Ok(new { success = false, message = "没有权限" });

            var dir = await _fileService.GetDirectoryAsync(form.DirId);
            if (dir == null) return Results.Ok(new { success = false, message = "目录不存在" });

            var targetPath = string.IsNullOrEmpty(form.Path) ? dir.Path : Path.Combine(dir.Path, form.Path);
            var folderPath = Path.Combine(targetPath, form.Name);

            if (Directory.Exists(folderPath))
                return Results.Ok(new { success = false, message = "文件夹已存在" });

            Directory.CreateDirectory(folderPath);
            return Results.Ok(new { success = true, message = "创建成功" });
        }).RequireAuthorization();

        app.MapGet("/api/files/preview", async (int dirId, string path, string? token, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == 0 && !string.IsNullOrEmpty(token))
            {
                var principal = Utils.JwtHelper.ValidateToken(token.Replace("Bearer ", ""), _config.JwtSecret);
                if (principal != null)
                    userId = int.Parse(principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
            }
            if (userId == 0) return Results.Unauthorized();

            var hasPerm = await _fileService.HasPermissionAsync(dirId, userId, "CanRead");
            if (!hasPerm) return Results.Ok(new { success = false, message = "没有权限" });

            var dir = await _fileService.GetDirectoryAsync(dirId);
            if (dir == null) return Results.Ok(new { success = false, message = "目录不存在" });

            var filePath = Path.GetFullPath(Path.Combine(dir.Path, path));
            if (!filePath.StartsWith(Path.GetFullPath(dir.Path), StringComparison.OrdinalIgnoreCase))
                return Results.Ok(new { success = false, message = "非法路径" });

            if (!File.Exists(filePath))
                return Results.Ok(new { success = false, message = "文件不存在" });

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var isImage = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg" }.Contains(ext);
            var isText = new[] { ".txt", ".log", ".md", ".json", ".xml", ".csv", ".ini", ".cfg", ".conf", ".yml", ".yaml", ".html", ".css", ".js", ".ts", ".py", ".java", ".c", ".cpp", ".h", ".cs", ".go", ".rs", ".sh", ".bat", ".sql" }.Contains(ext);
            var isPdf = ext == ".pdf";

            if (!isImage && !isText && !isPdf)
                return Results.Ok(new { success = false, message = "不支持预览此文件类型" });

            if (isPdf)
            {
                var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Results.File(stream, "application/pdf");
            }

            if (isImage)
            {
                var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Results.File(stream, GetMimeType(ext));
            }

            var textContent = await File.ReadAllTextAsync(filePath);
            return Results.Ok(new { success = true, data = new { content = textContent, name = Path.GetFileName(filePath), extension = ext } });
        });

        app.MapPost("/api/files/lock", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<LockFileRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null) return Results.Ok(new { success = false, message = "用户不存在" });

            var existing = await _repo.QueryFirstOrDefaultAsync<FileLockInfo>(
                "SELECT * FROM FileLocks WHERE DirectoryId = @DirId AND FilePath = @Path",
                new { DirId = form.DirectoryId, Path = form.FilePath });

            if (existing != null)
            {
                if (existing.UserId != userId)
                    return Results.Ok(new { success = false, message = $"文件已被 {existing.Username} 锁定", data = existing });

                await _repo.ExecuteAsync("DELETE FROM FileLocks WHERE Id = @Id", new { Id = existing.Id });
            }

            await _repo.ExecuteAsync(
                "INSERT INTO FileLocks (DirectoryId, FilePath, UserId, Username, ExpiresAt) VALUES (@DirId, @Path, @UserId, @Username, @ExpiresAt)",
                new { DirId = form.DirectoryId, Path = form.FilePath, UserId = userId, Username = user.Nickname ?? user.Username, ExpiresAt = (string?)null });

            return Results.Ok(new { success = true, message = "文件已锁定" });
        }).RequireAuthorization();

        app.MapDelete("/api/files/lock", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<LockFileRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var existing = await _repo.QueryFirstOrDefaultAsync<FileLockInfo>(
                "SELECT * FROM FileLocks WHERE DirectoryId = @DirId AND FilePath = @Path",
                new { DirId = form.DirectoryId, Path = form.FilePath });

            if (existing == null) return Results.Ok(new { success = true, message = "文件未锁定" });

            if (existing.UserId != userId)
                return Results.Ok(new { success = false, message = "只能解锁自己锁定的文件" });

            await _repo.ExecuteAsync("DELETE FROM FileLocks WHERE Id = @Id", new { Id = existing.Id });
            return Results.Ok(new { success = true, message = "文件已解锁" });
        }).RequireAuthorization();

        app.MapGet("/api/files/locks", async (HttpContext ctx) =>
        {
            var dirIdStr = ctx.Request.Query["dirId"].ToString();
            if (!int.TryParse(dirIdStr, out int dirId))
                return Results.Ok(new { success = false, message = "无效目录ID" });

            var locks = await _repo.QueryAsync<FileLockInfo>(
                "SELECT * FROM FileLocks WHERE DirectoryId = @DirId", new { DirId = dirId });

            return Results.Ok(new { success = true, data = locks });
        }).RequireAuthorization();

        app.MapGet("/api/guest/directories", async (HttpContext ctx) =>
        {
            if (!_config.AllowGuest)
                return Results.Ok(new { success = false, message = "访客模式未开启" });

            var dirs = await _fileService.GetAllDirectoriesAsync();
            var publicDirs = dirs.Where(d => d.Visibility == "public").Select(d => new
            {
                d.Id, d.Name, d.Description, d.Visibility,
                d.AllowUpload, d.AllowDelete, d.AllowRename, d.AllowMove
            });
            return Results.Ok(new { success = true, data = publicDirs });
        });

        app.MapGet("/api/guest/files", async (int dirId, string? path, HttpContext ctx) =>
        {
            if (!_config.AllowGuest)
                return Results.Ok(new { success = false, message = "访客模式未开启" });

            var dir = await _fileService.GetDirectoryAsync(dirId);
            if (dir == null || dir.Visibility != "public")
                return Results.Ok(new { success = false, message = "目录不存在或不公开" });

            var files = _fileService.ListFiles(dir.Path, path);
            return Results.Ok(new { success = true, data = files, dirPath = dir.Path });
        });

        app.MapGet("/api/guest/download", async (int dirId, string? path, HttpContext ctx) =>
        {
            if (!_config.AllowGuest)
                return Results.Ok(new { success = false, message = "访客模式未开启" });

            var dir = await _fileService.GetDirectoryAsync(dirId);
            if (dir == null || dir.Visibility != "public")
                return Results.Ok(new { success = false, message = "目录不存在或不公开" });

            var targetPath = string.IsNullOrEmpty(path) ? dir.Path : Path.Combine(dir.Path, path);
            if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
                return Results.NotFound();

            if (Directory.Exists(targetPath))
            {
                var zipName = new DirectoryInfo(targetPath).Name + ".zip";
                ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"{Uri.EscapeDataString(zipName)}\"";
                ctx.Response.ContentType = "application/zip";
                using var zipStream = new MemoryStream();
                ZipFile.CreateFromDirectory(targetPath, zipStream);
                await ctx.Response.Body.WriteAsync(zipStream.ToArray());
                return Results.Ok();
            }

            return SendFileWithRangeSupport(ctx, targetPath);
        });

        app.MapGet("/api/guest/preview", async (int dirId, string? path, HttpContext ctx) =>
        {
            if (!_config.AllowGuest)
                return Results.Ok(new { success = false, message = "访客模式未开启" });

            var dir = await _fileService.GetDirectoryAsync(dirId);
            if (dir == null || dir.Visibility != "public")
                return Results.Ok(new { success = false, message = "目录不存在或不公开" });

            var targetPath = string.IsNullOrEmpty(path) ? dir.Path : Path.Combine(dir.Path, path);
            if (!File.Exists(targetPath))
                return Results.Ok(new { success = false, message = "文件不存在" });

            var ext = Path.GetExtension(targetPath).ToLower();
            var imageExts = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg" };

            if (imageExts.Contains(ext))
            {
                var mimeType = GetMimeType(ext);
                return Results.File(targetPath, mimeType);
            }

            var textExts = new[] { ".txt", ".log", ".md", ".json", ".xml", ".csv", ".ini", ".cfg", ".conf", ".yml", ".yaml", ".html", ".css", ".js", ".ts", ".py", ".java", ".c", ".cpp", ".h", ".cs", ".go", ".rs", ".sh", ".bat", ".sql" };
            if (textExts.Contains(ext))
            {
                var textContent = await File.ReadAllTextAsync(targetPath);
                return Results.Ok(new { success = true, data = new { content = textContent, name = Path.GetFileName(targetPath), extension = ext } });
            }

            return Results.Ok(new { success = false, message = "不支持预览" });
        });

        app.MapGet("/api/guest/stream", async (int dirId, string? path, HttpContext ctx) =>
        {
            if (!_config.AllowGuest)
                return Results.Ok(new { success = false, message = "访客模式未开启" });

            var dir = await _fileService.GetDirectoryAsync(dirId);
            if (dir == null || dir.Visibility != "public")
                return Results.Ok(new { success = false, message = "目录不存在或不公开" });

            var targetPath = string.IsNullOrEmpty(path) ? dir.Path : Path.Combine(dir.Path, path);
            if (!File.Exists(targetPath))
                return Results.NotFound();

            return SendFileWithRangeSupport(ctx, targetPath);
        });
    }

    private static IResult SendFileWithRangeSupport(HttpContext ctx, string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var fileSize = fileInfo.Length;
        var mimeType = GetMimeType(fileInfo.Extension);

        var rangeHeader = ctx.Request.Headers.Range.ToString();
        if (string.IsNullOrEmpty(rangeHeader))
        {
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Results.File(stream, mimeType, fileInfo.Name, enableRangeProcessing: false);
        }

        var range = ParseRangeHeader(rangeHeader, fileSize);
        if (range == null)
        {
            ctx.Response.StatusCode = 416;
            ctx.Response.Headers.ContentRange = $"bytes */{fileSize}";
            return Results.Empty;
        }

        var (start, end) = range.Value;
        var contentLength = end - start + 1;

        ctx.Response.StatusCode = 206;
        ctx.Response.Headers.ContentType = mimeType;
        ctx.Response.Headers.ContentLength = contentLength;
        ctx.Response.Headers.ContentRange = $"bytes {start}-{end}/{fileSize}";
        ctx.Response.Headers.AcceptRanges = "bytes";

        if (start > 0)
        {
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            stream.Seek(start, SeekOrigin.Begin);
            return Results.Stream(stream, mimeType);
        }
        else
        {
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Results.Stream(stream, mimeType);
        }
    }

    private static (long Start, long End)? ParseRangeHeader(string rangeHeader, long fileSize)
    {
        if (!rangeHeader.StartsWith("bytes=")) return null;

        var rangeSpec = rangeHeader.Substring(6).Trim();
        var parts = rangeSpec.Split('-');
        if (parts.Length != 2) return null;

        long start, end;

        if (string.IsNullOrEmpty(parts[0]))
        {
            if (!long.TryParse(parts[1], out var suffix)) return null;
            start = Math.Max(0, fileSize - suffix);
            end = fileSize - 1;
        }
        else if (string.IsNullOrEmpty(parts[1]))
        {
            if (!long.TryParse(parts[0], out start)) return null;
            end = fileSize - 1;
        }
        else
        {
            if (!long.TryParse(parts[0], out start)) return null;
            if (!long.TryParse(parts[1], out end)) return null;
        }

        if (start > end || start >= fileSize) return null;
        end = Math.Min(end, fileSize - 1);

        return (start, end);
    }

    private static int GetUserId(HttpContext ctx)
    {
        var claim = ctx.User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null ? int.Parse(claim.Value) : 0;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        if (!Directory.Exists(destDir))
            Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);

        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }

    private static string GetMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".mov" => "video/quicktime",
            ".wmv" => "video/x-ms-wmv",
            ".flv" => "video/x-flv",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            ".aac" => "audio/aac",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/mp4",
            ".wma" => "audio/x-ms-wma",
            ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed",
            ".7z" => "application/x-7z-compressed",
            ".txt" => "text/plain",
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".xml" => "application/xml",
            _ => "application/octet-stream"
        };
    }
}

public record CreateDirectoryRequest(string Name, string Path, string? Description, string? Visibility, bool? AllowUpload, bool? AllowDelete, bool? AllowRename, bool? AllowMove);
public record UpdateDirectoryRequest(string? Name, string? Description, string? Visibility, bool? AllowUpload, bool? AllowDelete, bool? AllowRename, bool? AllowMove);
public record RenameFileRequest(int RecordId, int DirectoryId, string NewName, string? RelativePath);
public record DeleteFileRequest(int RecordId, int DirectoryId, string? RelativePath);
public record MoveFileRequest(int RecordId, int DirectoryId, int TargetDirectoryId, string? TargetSubPath, string? SourceRelativePath, string? SourceFileName);
public record ShareFileRequest(int DirectoryId, string? FilePath, string? Password, DateTime? ExpiresAt, int MaxDownloads);
public record CreateFolderRequest(int DirId, string? Path, string Name);
public record SetPermissionRequest(int DirectoryId, int UserId, bool CanRead, bool CanWrite, bool CanDelete, bool CanRename, bool CanMove);
public record LockFileRequest(int DirectoryId, string FilePath);

public class FileLockInfo
{
    public int Id { get; set; }
    public int DirectoryId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string? ExpiresAt { get; set; }
}
