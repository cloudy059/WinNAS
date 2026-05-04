using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using WinNASClient.Database;
using WinNASClient.Services;
using WinNASClient.Config;
using System.IO.Compression;
using Dapper;

namespace WinNASClient.HttpServer.Controllers;

public class EnhancedController
{
    private readonly DbRepository _repo;
    private readonly FileService _fileService;
    private readonly SystemService _systemService;
    private readonly AuthService _authService;
    private readonly AppConfig _config;

    public EnhancedController(DbRepository repo, FileService fileService, SystemService systemService, AuthService authService, AppConfig config)
    {
        _repo = repo;
        _fileService = fileService;
        _systemService = systemService;
        _authService = authService;
        _config = config;
    }

    public void Register(WebApplication app)
    {
        RegisterSearch(app);
        RegisterRecycleBin(app);
        RegisterFavorites(app);
        RegisterRecentAccess(app);
        RegisterFileTags(app);
        RegisterBatchOperations(app);
        RegisterDownloadStats(app);
        RegisterCompress(app);
        RegisterClipboard(app);
        RegisterNotifications(app);
        RegisterBackup(app);
    }

    #region Search
    private void RegisterSearch(WebApplication app)
    {
        app.MapGet("/api/files/search", async (string keyword, string? ext, int? dirId, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (string.IsNullOrWhiteSpace(keyword))
                return Results.Ok(new { success = true, data = new object[0] });

            var dirs = await _fileService.GetSharedDirectoriesAsync(userId);
            if (dirId.HasValue)
            {
                var dir = dirs.FirstOrDefault(d => d.Id == dirId.Value);
                if (dir != null) dirs = new List<Models.SharedDirectory> { dir };
            }

            var results = new List<object>();
            var keywordLower = keyword.ToLowerInvariant();
            var allowedExts = string.IsNullOrWhiteSpace(ext) ? null : ext.Split(',').Select(e => e.Trim().ToLowerInvariant()).ToHashSet();

            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir.Path)) continue;
                try
                {
                    SearchDirectory(dir.Path, dir.Path, dir.Id, keywordLower, allowedExts, results, 200);
                }
                catch { }
                if (results.Count >= 200) break;
            }

            return Results.Ok(new { success = true, data = results });
        }).RequireAuthorization();
    }

    private void SearchDirectory(string rootPath, string currentPath, int dirId, string keyword, HashSet<string>? allowedExts, List<object> results, int maxResults)
    {
        if (results.Count >= maxResults) return;

        try
        {
            foreach (var file in Directory.GetFiles(currentPath))
            {
                if (results.Count >= maxResults) return;
                var name = Path.GetFileName(file);
                if (!name.ToLowerInvariant().Contains(keyword)) continue;

                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (allowedExts != null && !allowedExts.Contains(ext)) continue;

                var fi = new FileInfo(file);
                results.Add(new
                {
                    name,
                    relativePath = file.Substring(rootPath.Length).TrimStart('\\', '/'),
                    directoryId = dirId,
                    isDirectory = false,
                    size = fi.Length,
                    extension = ext,
                    lastModified = fi.LastWriteTimeUtc
                });
            }

            foreach (var dir in Directory.GetDirectories(currentPath))
            {
                if (results.Count >= maxResults) return;
                var name = Path.GetFileName(dir);
                if (name.ToLowerInvariant().Contains(keyword))
                {
                    results.Add(new
                    {
                        name,
                        relativePath = dir.Substring(rootPath.Length).TrimStart('\\', '/'),
                        directoryId = dirId,
                        isDirectory = true,
                        size = 0L,
                        extension = "",
                        lastModified = Directory.GetLastWriteTimeUtc(dir)
                    });
                }
                SearchDirectory(rootPath, dir, dirId, keyword, allowedExts, results, maxResults);
            }
        }
        catch (UnauthorizedAccessException) { }
    }
    #endregion

    #region RecycleBin
    private void RegisterRecycleBin(WebApplication app)
    {
        app.MapGet("/api/recycle/list", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var items = await _repo.QueryAsync<RecycleBinItem>(
                "SELECT * FROM RecycleBin WHERE UserId = @UserId ORDER BY CreatedAt DESC", new { UserId = userId });
            return Results.Ok(new { success = true, data = items });
        }).RequireAuthorization();

        app.MapPost("/api/recycle/restore", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<IdRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var item = await _repo.QueryFirstOrDefaultAsync<RecycleBinItem>(
                "SELECT * FROM RecycleBin WHERE Id = @Id AND UserId = @UserId", new { Id = form.Id, UserId = userId });
            if (item == null) return Results.Ok(new { success = false, message = "记录不存在" });

            try
            {
                if (item.IsDirectory)
                {
                    if (Directory.Exists(item.RecycledPath))
                    {
                        if (!Directory.Exists(item.OriginalPath))
                        {
                            CopyDirectory(item.RecycledPath, item.OriginalPath);
                            Directory.Delete(item.RecycledPath, true);
                        }
                        else
                            CopyDirectory(item.RecycledPath, item.OriginalPath);
                    }
                }
                else
                {
                    if (File.Exists(item.RecycledPath))
                    {
                        var dir = Path.GetDirectoryName(item.OriginalPath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        if (!File.Exists(item.OriginalPath))
                        {
                            File.Copy(item.RecycledPath, item.OriginalPath, true);
                            File.Delete(item.RecycledPath);
                        }
                        else
                            File.Copy(item.RecycledPath, item.OriginalPath, true);
                    }
                }

                await _repo.ExecuteAsync("DELETE FROM RecycleBin WHERE Id = @Id", new { Id = form.Id });
                return Results.Ok(new { success = true, message = "恢复成功" });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = $"恢复失败: {ex.Message}" });
            }
        }).RequireAuthorization();

        app.MapDelete("/api/recycle/purge", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<IdRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var item = await _repo.QueryFirstOrDefaultAsync<RecycleBinItem>(
                "SELECT * FROM RecycleBin WHERE Id = @Id AND UserId = @UserId", new { Id = form.Id, UserId = userId });
            if (item == null) return Results.Ok(new { success = false, message = "记录不存在" });

            try
            {
                if (item.IsDirectory && Directory.Exists(item.RecycledPath))
                    Directory.Delete(item.RecycledPath, true);
                else if (!item.IsDirectory && File.Exists(item.RecycledPath))
                    File.Delete(item.RecycledPath);

                await _repo.ExecuteAsync("DELETE FROM RecycleBin WHERE Id = @Id", new { Id = form.Id });
                return Results.Ok(new { success = true, message = "已彻底删除" });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = $"删除失败: {ex.Message}" });
            }
        }).RequireAuthorization();

        app.MapDelete("/api/recycle/empty", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var items = await _repo.QueryAsync<RecycleBinItem>(
                "SELECT * FROM RecycleBin WHERE UserId = @UserId", new { UserId = userId });

            foreach (var item in items)
            {
                try
                {
                    if (item.IsDirectory && Directory.Exists(item.RecycledPath))
                        Directory.Delete(item.RecycledPath, true);
                    else if (!item.IsDirectory && File.Exists(item.RecycledPath))
                        File.Delete(item.RecycledPath);
                }
                catch { }
            }

            await _repo.ExecuteAsync("DELETE FROM RecycleBin WHERE UserId = @UserId", new { UserId = userId });
            return Results.Ok(new { success = true, message = "回收站已清空" });
        }).RequireAuthorization();
    }
    #endregion

    #region Favorites
    private void RegisterFavorites(WebApplication app)
    {
        app.MapGet("/api/favorites/list", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var items = await _repo.QueryAsync<FavoriteItem>(
                "SELECT * FROM Favorites WHERE UserId = @UserId ORDER BY CreatedAt DESC", new { UserId = userId });
            var dirs = await _fileService.GetAllDirectoriesAsync();
            foreach (var item in items)
            {
                var dir = dirs.FirstOrDefault(d => d.Id == item.DirectoryId);
                if (dir != null)
                {
                    var fullPath = Path.GetFullPath(Path.Combine(dir.Path, item.FilePath ?? ""));
                    item.FullPath = fullPath;
                }
            }
            return Results.Ok(new { success = true, data = items });
        }).RequireAuthorization();

        app.MapPost("/api/favorites/add", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<FavoriteRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var existing = await _repo.QueryFirstOrDefaultAsync<int>(
                "SELECT Id FROM Favorites WHERE UserId = @UserId AND DirectoryId = @DirId AND FilePath = @Path",
                new { UserId = userId, DirId = form.DirectoryId, Path = form.FilePath ?? "" });
            if (existing > 0) return Results.Ok(new { success = true, message = "已收藏" });

            await _repo.ExecuteAsync(
                "INSERT INTO Favorites (UserId, DirectoryId, FilePath, FileName, IsDirectory) VALUES (@UserId, @DirId, @Path, @Name, @IsDir)",
                new { UserId = userId, DirId = form.DirectoryId, Path = form.FilePath ?? "", Name = form.FileName, IsDir = form.IsDirectory ? 1 : 0 });
            return Results.Ok(new { success = true, message = "收藏成功" });
        }).RequireAuthorization();

        app.MapDelete("/api/favorites/remove", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<FavoriteRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            await _repo.ExecuteAsync(
                "DELETE FROM Favorites WHERE UserId = @UserId AND DirectoryId = @DirId AND FilePath = @Path",
                new { UserId = userId, DirId = form.DirectoryId, Path = form.FilePath ?? "" });
            return Results.Ok(new { success = true, message = "已取消收藏" });
        }).RequireAuthorization();

        app.MapGet("/api/favorites/check", async (int dirId, string? path, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var exists = await _repo.QueryFirstOrDefaultAsync<int>(
                "SELECT Id FROM Favorites WHERE UserId = @UserId AND DirectoryId = @DirId AND FilePath = @Path",
                new { UserId = userId, DirId = dirId, Path = path ?? "" });
            return Results.Ok(new { success = true, data = exists > 0 });
        }).RequireAuthorization();
    }
    #endregion

    #region RecentAccess
    private void RegisterRecentAccess(WebApplication app)
    {
        app.MapGet("/api/recent/list", async (int? limit, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var l = limit ?? 20;
            var items = await _repo.QueryAsync<RecentAccessItem>(
                "SELECT * FROM RecentAccess WHERE UserId = @UserId ORDER BY CreatedAt DESC LIMIT @Limit",
                new { UserId = userId, Limit = l });
            return Results.Ok(new { success = true, data = items });
        }).RequireAuthorization();

        app.MapPost("/api/recent/record", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<RecentAccessRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            await _repo.ExecuteAsync(
                "DELETE FROM RecentAccess WHERE UserId = @UserId AND DirectoryId = @DirId AND FilePath = @Path AND AccessType = @Type",
                new { UserId = userId, DirId = form.DirectoryId, Path = form.FilePath ?? "", Type = form.AccessType });
            await _repo.ExecuteAsync(
                "INSERT INTO RecentAccess (UserId, DirectoryId, FilePath, FileName, AccessType) VALUES (@UserId, @DirId, @Path, @Name, @Type)",
                new { UserId = userId, DirId = form.DirectoryId, Path = form.FilePath ?? "", Name = form.FileName, Type = form.AccessType });
            return Results.Ok(new { success = true });
        }).RequireAuthorization();
    }
    #endregion

    #region FileTags
    private void RegisterFileTags(WebApplication app)
    {
        app.MapGet("/api/tags/list", async (int dirId, string path, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var tags = await _repo.QueryAsync<FileTagItem>(
                "SELECT * FROM FileTags WHERE DirectoryId = @DirId AND FilePath = @Path AND UserId = @UserId",
                new { DirId = dirId, Path = path, UserId = userId });
            return Results.Ok(new { success = true, data = tags });
        }).RequireAuthorization();

        app.MapGet("/api/tags/batch", async (int dirId, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var tags = await _repo.QueryAsync<FileTagItem>(
                "SELECT * FROM FileTags WHERE DirectoryId = @DirId AND UserId = @UserId",
                new { DirId = dirId, UserId = userId });
            return Results.Ok(new { success = true, data = tags });
        }).RequireAuthorization();

        app.MapPost("/api/tags/set", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<FileTagRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            await _repo.ExecuteAsync(
                "DELETE FROM FileTags WHERE DirectoryId = @DirId AND FilePath = @Path AND UserId = @UserId",
                new { DirId = form.DirectoryId, Path = form.FilePath, UserId = userId });

            if (!string.IsNullOrWhiteSpace(form.Tag) || !string.IsNullOrWhiteSpace(form.Note))
            {
                await _repo.ExecuteAsync(
                    "INSERT INTO FileTags (DirectoryId, FilePath, UserId, Tag, Color, Note) VALUES (@DirId, @Path, @UserId, @Tag, @Color, @Note)",
                    new { DirId = form.DirectoryId, Path = form.FilePath, UserId = userId, Tag = form.Tag ?? "", Color = form.Color ?? "", Note = form.Note ?? "" });
            }
            return Results.Ok(new { success = true, message = "标签已更新" });
        }).RequireAuthorization();
    }
    #endregion

    #region BatchOperations
    private void RegisterBatchOperations(WebApplication app)
    {
        app.MapPost("/api/files/batch-delete", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<BatchFileRequest>();
            if (form == null || form.Files == null || form.Files.Count == 0)
                return Results.Ok(new { success = false, message = "无效请求" });

            var successCount = 0;
            var failCount = 0;
            foreach (var f in form.Files)
            {
                var hasPerm = await _fileService.HasPermissionAsync(f.DirectoryId, userId, "CanDelete");
                if (!hasPerm) { failCount++; continue; }

                var dir = await _fileService.GetDirectoryAsync(f.DirectoryId);
                if (dir == null) { failCount++; continue; }

                var fullPath = Path.GetFullPath(Path.Combine(dir.Path, f.RelativePath ?? ""));
                if (!fullPath.StartsWith(Path.GetFullPath(dir.Path), StringComparison.OrdinalIgnoreCase))
                    { failCount++; continue; }

                try
                {
                    var recycledDir = Path.Combine(Path.GetTempPath(), "WinNAS_Recycle", Guid.NewGuid().ToString("N"));
                    if (File.Exists(fullPath))
                    {
                        Directory.CreateDirectory(recycledDir);
                        var fileSize = new FileInfo(fullPath).Length;
                        var recycledPath = Path.Combine(recycledDir, Path.GetFileName(fullPath));
                        File.Copy(fullPath, recycledPath, true);
                        File.Delete(fullPath);
                        await _repo.ExecuteAsync(
                            "INSERT INTO RecycleBin (DirectoryId, FileName, OriginalPath, RecycledPath, IsDirectory, Size, UserId, Username) VALUES (@DirId, @Name, @OrigPath, @RecycPath, 0, @Size, @UserId, @Username)",
                            new { DirId = f.DirectoryId, Name = Path.GetFileName(fullPath), OrigPath = fullPath, RecycPath = recycledPath, Size = fileSize, UserId = userId, Username = "" });
                    }
                    else if (Directory.Exists(fullPath))
                    {
                        var recycledPath = Path.Combine(recycledDir, Path.GetFileName(fullPath));
                        CopyDirectory(fullPath, recycledPath);
                        Directory.Delete(fullPath, true);
                        await _repo.ExecuteAsync(
                            "INSERT INTO RecycleBin (DirectoryId, FileName, OriginalPath, RecycledPath, IsDirectory, Size, UserId, Username) VALUES (@DirId, @Name, @OrigPath, @RecycPath, 1, 0, @UserId, @Username)",
                            new { DirId = f.DirectoryId, Name = Path.GetFileName(fullPath), OrigPath = fullPath, RecycPath = recycledPath, UserId = userId, Username = "" });
                    }
                    successCount++;
                }
                catch { failCount++; }
            }
            return Results.Ok(new { success = true, message = $"成功{successCount}个，失败{failCount}个" });
        }).RequireAuthorization();

        app.MapPost("/api/files/batch-download", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<BatchFileRequest>();
            if (form == null || form.Files == null || form.Files.Count == 0)
                return Results.Ok(new { success = false, message = "无效请求" });

            var hasAnyPerm = false;
            var filePaths = new List<string>();
            foreach (var f in form.Files)
            {
                var hasPerm = await _fileService.HasPermissionAsync(f.DirectoryId, userId, "CanRead");
                if (!hasPerm) continue;
                hasAnyPerm = true;

                var dir = await _fileService.GetDirectoryAsync(f.DirectoryId);
                if (dir == null) continue;

                var fullPath = Path.GetFullPath(Path.Combine(dir.Path, f.RelativePath ?? ""));
                if (!fullPath.StartsWith(Path.GetFullPath(dir.Path), StringComparison.OrdinalIgnoreCase)) continue;

                if (File.Exists(fullPath)) filePaths.Add(fullPath);
                else if (Directory.Exists(fullPath)) filePaths.Add(fullPath);
            }

            if (!hasAnyPerm || filePaths.Count == 0)
                return Results.Ok(new { success = false, message = "没有可下载的文件" });

            var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                foreach (var fp in filePaths)
                {
                    if (File.Exists(fp))
                        archive.CreateEntryFromFile(fp, Path.GetFileName(fp));
                    else if (Directory.Exists(fp))
                    {
                        foreach (var file in Directory.GetFiles(fp, "*", SearchOption.AllDirectories))
                        {
                            var entryName = Path.Combine(Path.GetFileName(fp), Path.GetRelativePath(fp, file));
                            archive.CreateEntryFromFile(file, entryName);
                        }
                    }
                }
            }
            ms.Position = 0;
            return Results.File(ms, "application/zip", $"batch_download_{DateTime.Now:yyyyMMddHHmmss}.zip");
        }).RequireAuthorization();

        app.MapPost("/api/files/batch-move", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<BatchMoveRequest>();
            if (form == null || form.Files == null || form.Files.Count == 0)
                return Results.Ok(new { success = false, message = "无效请求" });

            var targetDir = await _fileService.GetDirectoryAsync(form.TargetDirectoryId);
            if (targetDir == null) return Results.Ok(new { success = false, message = "目标目录不存在" });

            var targetPath = string.IsNullOrEmpty(form.TargetSubPath) ? targetDir.Path : Path.Combine(targetDir.Path, form.TargetSubPath);
            if (!Directory.Exists(targetPath)) Directory.CreateDirectory(targetPath);

            var successCount = 0;
            var failCount = 0;
            foreach (var f in form.Files)
            {
                var hasPerm = await _fileService.HasPermissionAsync(f.DirectoryId, userId, "CanMove");
                if (!hasPerm) { failCount++; continue; }

                var sourceDir = await _fileService.GetDirectoryAsync(f.DirectoryId);
                if (sourceDir == null) { failCount++; continue; }

                var sourcePath = Path.GetFullPath(Path.Combine(sourceDir.Path, f.RelativePath ?? ""));
                if (!sourcePath.StartsWith(Path.GetFullPath(sourceDir.Path), StringComparison.OrdinalIgnoreCase))
                    { failCount++; continue; }

                var destPath = Path.Combine(targetPath, Path.GetFileName(sourcePath));
                try
                {
                    if (File.Exists(sourcePath)) File.Move(sourcePath, destPath, true);
                    else if (Directory.Exists(sourcePath)) Directory.Move(sourcePath, destPath);
                    successCount++;
                }
                catch { failCount++; }
            }
            return Results.Ok(new { success = true, message = $"成功{successCount}个，失败{failCount}个" });
        }).RequireAuthorization();
    }
    #endregion

    #region DownloadStats
    private void RegisterDownloadStats(WebApplication app)
    {
        app.MapGet("/api/stats/downloads", async (int? limit, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null || user.Role != "admin")
                return Results.Ok(new { success = false, message = "仅管理员可查看" });

            var l = limit ?? 20;
            var stats = await _repo.QueryAsync<DownloadStatItem>(
                "SELECT * FROM DownloadStats ORDER BY DownloadCount DESC LIMIT @Limit", new { Limit = l });
            return Results.Ok(new { success = true, data = stats });
        }).RequireAuthorization();
    }

    public async Task RecordDownloadAsync(int dirId, string filePath, int userId)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);
            var existing = await _repo.QueryFirstOrDefaultAsync<DownloadStatItem>(
                "SELECT * FROM DownloadStats WHERE DirectoryId = @DirId AND FilePath = @Path",
                new { DirId = dirId, Path = filePath });
            if (existing != null)
            {
                await _repo.ExecuteAsync(
                    "UPDATE DownloadStats SET DownloadCount = DownloadCount + 1, LastDownloadedAt = datetime('now') WHERE Id = @Id",
                    new { Id = existing.Id });
            }
            else
            {
                await _repo.ExecuteAsync(
                    "INSERT INTO DownloadStats (DirectoryId, FilePath, FileName, UserId, DownloadCount) VALUES (@DirId, @Path, @Name, @UserId, 1)",
                    new { DirId = dirId, Path = filePath, Name = fileName, UserId = userId });
            }
        }
        catch { }
    }
    #endregion

    #region Compress
    private void RegisterCompress(WebApplication app)
    {
        app.MapPost("/api/files/compress", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<CompressRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var hasPerm = await _fileService.HasPermissionAsync(form.DirectoryId, userId, "CanWrite");
            if (!hasPerm) return Results.Ok(new { success = false, message = "没有权限" });

            var dir = await _fileService.GetDirectoryAsync(form.DirectoryId);
            if (dir == null) return Results.Ok(new { success = false, message = "目录不存在" });

            var targetDir = string.IsNullOrEmpty(form.TargetPath) ? dir.Path : Path.Combine(dir.Path, form.TargetPath);
            var zipPath = Path.Combine(targetDir, form.ZipName.EndsWith(".zip") ? form.ZipName : form.ZipName + ".zip");

            try
            {
                using var ms = new MemoryStream();
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach (var f in form.Files)
                    {
                        var fullPath = Path.GetFullPath(Path.Combine(dir.Path, f));
                        if (!fullPath.StartsWith(Path.GetFullPath(dir.Path), StringComparison.OrdinalIgnoreCase)) continue;

                        if (File.Exists(fullPath))
                            archive.CreateEntryFromFile(fullPath, Path.GetFileName(fullPath));
                        else if (Directory.Exists(fullPath))
                        {
                            foreach (var file in Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories))
                            {
                                var entryName = Path.Combine(Path.GetFileName(fullPath), Path.GetRelativePath(fullPath, file));
                                archive.CreateEntryFromFile(file, entryName);
                            }
                        }
                    }
                }
                ms.Position = 0;
                using var fs = File.Create(zipPath);
                await ms.CopyToAsync(fs);
                return Results.Ok(new { success = true, message = "压缩完成" });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = $"压缩失败: {ex.Message}" });
            }
        }).RequireAuthorization();

        app.MapPost("/api/files/extract", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<ExtractRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var hasPerm = await _fileService.HasPermissionAsync(form.DirectoryId, userId, "CanWrite");
            if (!hasPerm) return Results.Ok(new { success = false, message = "没有权限" });

            var dir = await _fileService.GetDirectoryAsync(form.DirectoryId);
            if (dir == null) return Results.Ok(new { success = false, message = "目录不存在" });

            var zipPath = Path.GetFullPath(Path.Combine(dir.Path, form.FilePath));
            if (!zipPath.StartsWith(Path.GetFullPath(dir.Path), StringComparison.OrdinalIgnoreCase))
                return Results.Ok(new { success = false, message = "非法路径" });
            if (!File.Exists(zipPath))
                return Results.Ok(new { success = false, message = "文件不存在" });

            var targetDir = string.IsNullOrEmpty(form.TargetPath)
                ? Path.Combine(Path.GetDirectoryName(zipPath)!, Path.GetFileNameWithoutExtension(zipPath))
                : Path.Combine(dir.Path, form.TargetPath);

            try
            {
                ZipFile.ExtractToDirectory(zipPath, targetDir, true);
                return Results.Ok(new { success = true, message = "解压完成" });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = $"解压失败: {ex.Message}" });
            }
        }).RequireAuthorization();
    }
    #endregion

    #region Clipboard
    private void RegisterClipboard(WebApplication app)
    {
        app.MapPost("/api/files/copy", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<CopyRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var hasReadPerm = await _fileService.HasPermissionAsync(form.SourceDirectoryId, userId, "CanRead");
            if (!hasReadPerm) return Results.Ok(new { success = false, message = "没有源目录读取权限" });

            var hasWritePerm = await _fileService.HasPermissionAsync(form.TargetDirectoryId, userId, "CanWrite");
            if (!hasWritePerm) return Results.Ok(new { success = false, message = "没有目标目录写入权限" });

            var sourceDir = await _fileService.GetDirectoryAsync(form.SourceDirectoryId);
            var targetDir = await _fileService.GetDirectoryAsync(form.TargetDirectoryId);
            if (sourceDir == null || targetDir == null) return Results.Ok(new { success = false, message = "目录不存在" });

            var targetPath = string.IsNullOrEmpty(form.TargetSubPath) ? targetDir.Path : Path.Combine(targetDir.Path, form.TargetSubPath);
            if (!Directory.Exists(targetPath)) Directory.CreateDirectory(targetPath);

            var successCount = 0;
            var failCount = 0;
            foreach (var relPath in form.RelativePaths)
            {
                var sourcePath = Path.GetFullPath(Path.Combine(sourceDir.Path, relPath));
                if (!sourcePath.StartsWith(Path.GetFullPath(sourceDir.Path), StringComparison.OrdinalIgnoreCase))
                    { failCount++; continue; }

                var destPath = Path.Combine(targetPath, Path.GetFileName(sourcePath));
                try
                {
                    if (File.Exists(sourcePath)) { File.Copy(sourcePath, destPath, true); successCount++; }
                    else if (Directory.Exists(sourcePath)) { CopyDirectory(sourcePath, destPath); successCount++; }
                    else failCount++;
                }
                catch { failCount++; }
            }
            return Results.Ok(new { success = true, message = $"复制{successCount}个，失败{failCount}个" });
        }).RequireAuthorization();
    }
    #endregion

    #region Notifications
    private void RegisterNotifications(WebApplication app)
    {
        app.MapGet("/api/notifications/list", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var items = await _repo.QueryAsync<NotificationItem>(
                "SELECT * FROM Notifications WHERE UserId = @UserId ORDER BY CreatedAt DESC LIMIT 50", new { UserId = userId });
            var unread = await _repo.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Notifications WHERE UserId = @UserId AND IsRead = 0", new { UserId = userId });
            return Results.Ok(new { success = true, data = items, unread });
        }).RequireAuthorization();

        app.MapPost("/api/notifications/read", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<IdRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            if (form.Id > 0)
                await _repo.ExecuteAsync("UPDATE Notifications SET IsRead = 1 WHERE Id = @Id AND UserId = @UserId", new { Id = form.Id, UserId = userId });
            else
                await _repo.ExecuteAsync("UPDATE Notifications SET IsRead = 1 WHERE UserId = @UserId", new { UserId = userId });
            return Results.Ok(new { success = true });
        }).RequireAuthorization();

        app.MapDelete("/api/notifications/clear", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            await _repo.ExecuteAsync("DELETE FROM Notifications WHERE UserId = @UserId", new { UserId = userId });
            return Results.Ok(new { success = true, message = "已清空通知" });
        }).RequireAuthorization();
    }

    public async Task AddNotificationAsync(int userId, string type, string title, string content)
    {
        try
        {
            await _repo.ExecuteAsync(
                "INSERT INTO Notifications (UserId, Type, Title, Content) VALUES (@UserId, @Type, @Title, @Content)",
                new { UserId = userId, Type = type, Title = title, Content = content });
        }
        catch { }
    }
    #endregion

    #region Backup
    private void RegisterBackup(WebApplication app)
    {
        app.MapPost("/api/backup/create", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null || user.Role != "admin")
                return Results.Ok(new { success = false, message = "仅管理员可操作" });

            try
            {
                var backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
                if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupFile = Path.Combine(backupDir, $"winnas_backup_{timestamp}.zip");

                using (var fs = File.Create(backupFile))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var dbPath = _config.DbPath;
                    if (File.Exists(dbPath))
                    {
                        var tempDb = Path.Combine(Path.GetTempPath(), $"winnas_backup_{timestamp}.db");
                        File.Copy(dbPath, tempDb, true);
                        try
                        {
                            archive.CreateEntryFromFile(tempDb, "winnas.db");
                        }
                        finally
                        {
                            try { File.Delete(tempDb); } catch { }
                        }
                    }

                    var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                    if (File.Exists(configPath))
                        archive.CreateEntryFromFile(configPath, "appsettings.json");
                }

                return Results.Ok(new { success = true, message = "备份成功", data = new { path = backupFile, size = new FileInfo(backupFile).Length } });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = $"备份失败: {ex.Message}" });
            }
        }).RequireAuthorization();

        app.MapPost("/api/backup/restore", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null || user.Role != "admin")
                return Results.Ok(new { success = false, message = "仅管理员可操作" });

            try
            {
                var form = await ctx.Request.ReadFormAsync();
                var file = form.Files.FirstOrDefault();
                if (file == null) return Results.Ok(new { success = false, message = "请选择备份文件" });

                var tempDir = Path.Combine(Path.GetTempPath(), "WinNAS_Restore_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                var tempFile = Path.Combine(tempDir, file.FileName);
                using (var stream = File.Create(tempFile))
                    await file.CopyToAsync(stream);

                ZipFile.ExtractToDirectory(tempFile, tempDir, true);

                var dbSource = Path.Combine(tempDir, "winnas.db");
                if (File.Exists(dbSource))
                {
                    var dbDest = _config.DbPath;
                    File.Copy(dbSource, dbDest, true);
                }

                var configSource = Path.Combine(tempDir, "appsettings.json");
                if (File.Exists(configSource))
                {
                    var configDest = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                    File.Copy(configSource, configDest, true);
                }

                try { Directory.Delete(tempDir, true); } catch { }

                return Results.Ok(new { success = true, message = "恢复成功，请重启应用" });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = $"恢复失败: {ex.Message}" });
            }
        }).RequireAuthorization();

        app.MapGet("/api/backup/list", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null || user.Role != "admin")
                return Results.Ok(new { success = false, message = "仅管理员可查看" });

            var backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
            if (!Directory.Exists(backupDir))
                return Results.Ok(new { success = true, data = new object[0] });

            var files = Directory.GetFiles(backupDir, "*.zip")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .Select(f => new { name = f.Name, size = f.Length, createdAt = f.CreationTime })
                .ToList();

            return Results.Ok(new { success = true, data = files });
        }).RequireAuthorization();

        app.MapGet("/api/backup/download", async (string name, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null || user.Role != "admin")
                return Results.Ok(new { success = false, message = "仅管理员可操作" });

            var backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
            var filePath = Path.GetFullPath(Path.Combine(backupDir, name));
            if (!filePath.StartsWith(Path.GetFullPath(backupDir), StringComparison.OrdinalIgnoreCase))
                return Results.Ok(new { success = false, message = "非法路径" });
            if (!File.Exists(filePath))
                return Results.Ok(new { success = false, message = "文件不存在" });

            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Results.File(stream, "application/zip", name);
        }).RequireAuthorization();

        app.MapDelete("/api/backup/delete", async (string name, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null || user.Role != "admin")
                return Results.Ok(new { success = false, message = "仅管理员可操作" });

            var backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
            var filePath = Path.GetFullPath(Path.Combine(backupDir, name));
            if (!filePath.StartsWith(Path.GetFullPath(backupDir), StringComparison.OrdinalIgnoreCase))
                return Results.Ok(new { success = false, message = "非法路径" });
            if (!File.Exists(filePath))
                return Results.Ok(new { success = false, message = "文件不存在" });

            try
            {
                File.Delete(filePath);
                return Results.Ok(new { success = true, message = "备份已删除" });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = $"删除失败: {ex.Message}" });
            }
        }).RequireAuthorization();
    }
    #endregion

    private static int GetUserId(HttpContext ctx)
    {
        var claim = ctx.User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null ? int.Parse(claim.Value) : 0;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }
}

public class RecycleBinItem { public int Id { get; set; } public int DirectoryId { get; set; } public string FileName { get; set; } = ""; public string OriginalPath { get; set; } = ""; public string RecycledPath { get; set; } = ""; public bool IsDirectory { get; set; } public long Size { get; set; } public int UserId { get; set; } public string Username { get; set; } = ""; public string CreatedAt { get; set; } = ""; }
public class FavoriteItem { public int Id { get; set; } public int UserId { get; set; } public int DirectoryId { get; set; } public string FilePath { get; set; } = ""; public string FileName { get; set; } = ""; public bool IsDirectory { get; set; } public string CreatedAt { get; set; } = ""; public string FullPath { get; set; } = ""; }
public class RecentAccessItem { public int Id { get; set; } public int UserId { get; set; } public int DirectoryId { get; set; } public string FilePath { get; set; } = ""; public string FileName { get; set; } = ""; public string AccessType { get; set; } = ""; public string CreatedAt { get; set; } = ""; }
public class FileTagItem { public int Id { get; set; } public int DirectoryId { get; set; } public string FilePath { get; set; } = ""; public int UserId { get; set; } public string Tag { get; set; } = ""; public string Color { get; set; } = ""; public string Note { get; set; } = ""; }
public class DownloadStatItem { public int Id { get; set; } public int DirectoryId { get; set; } public string FilePath { get; set; } = ""; public string FileName { get; set; } = ""; public int UserId { get; set; } public int DownloadCount { get; set; } public string LastDownloadedAt { get; set; } = ""; }
public class NotificationItem { public int Id { get; set; } public int UserId { get; set; } public string Type { get; set; } = ""; public string Title { get; set; } = ""; public string Content { get; set; } = ""; public bool IsRead { get; set; } public string CreatedAt { get; set; } = ""; }

public class IdRequest { public int Id { get; set; } }
public class FavoriteRequest { public int DirectoryId { get; set; } public string? FilePath { get; set; } public string FileName { get; set; } = ""; public bool IsDirectory { get; set; } }
public class RecentAccessRequest { public int DirectoryId { get; set; } public string? FilePath { get; set; } public string FileName { get; set; } = ""; public string AccessType { get; set; } = ""; }
public class FileTagRequest { public int DirectoryId { get; set; } public string FilePath { get; set; } = ""; public string? Tag { get; set; } public string? Color { get; set; } public string? Note { get; set; } }
public class BatchFileRequest { public List<BatchFileInfo> Files { get; set; } = new(); }
public class BatchFileInfo { public int DirectoryId { get; set; } public string? RelativePath { get; set; } }
public class BatchMoveRequest { public List<BatchFileInfo> Files { get; set; } = new(); public int TargetDirectoryId { get; set; } public string? TargetSubPath { get; set; } }
public class CompressRequest { public int DirectoryId { get; set; } public string? TargetPath { get; set; } public string ZipName { get; set; } = ""; public List<string> Files { get; set; } = new(); }
public class ExtractRequest { public int DirectoryId { get; set; } public string FilePath { get; set; } = ""; public string? TargetPath { get; set; } }
public class CopyRequest { public int SourceDirectoryId { get; set; } public int TargetDirectoryId { get; set; } public string? TargetSubPath { get; set; } public List<string> RelativePaths { get; set; } = new(); }
