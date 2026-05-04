using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using WinNASClient.Services;
using System.IO.Compression;

namespace WinNASClient.HttpServer.Controllers;

public class ShareController
{
    private readonly ShareService _shareService;
    private readonly FileService _fileService;
    private readonly SystemService _systemService;

    public ShareController(ShareService shareService, FileService fileService, SystemService systemService)
    {
        _shareService = shareService;
        _fileService = fileService;
        _systemService = systemService;
    }

    public void Register(WebApplication app)
    {
        app.MapPost("/api/shares", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<CreateShareRequest>();
            if (form == null) return Results.BadRequest(new { success = false, message = "无效请求" });

            var dir = await _fileService.GetDirectoryAsync(form.DirectoryId);
            if (dir == null) return Results.NotFound(new { success = false, message = "目录不存在" });

            var sharePath = string.IsNullOrWhiteSpace(form.Path) ? dir.Path : form.Path;

            var link = await _shareService.CreateShareLinkAsync(
                form.DirectoryId, sharePath, userId,
                form.Password, form.ExpiresAt, form.MaxDownloads);

            await _systemService.LogOperationAsync(userId, "", "create_share", link.Code, "", ctx.Connection.RemoteIpAddress?.ToString() ?? "");

            return Results.Ok(new { success = true, data = link });
        }).RequireAuthorization();

        app.MapGet("/api/shares", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var links = await _shareService.GetShareLinksByUserAsync(userId);
            return Results.Ok(new { success = true, data = links });
        }).RequireAuthorization();

        app.MapGet("/api/shares/{code}", async (string code, HttpContext ctx) =>
        {
            var link = await _shareService.GetShareLinkAsync(code);
            if (link == null) return Results.NotFound(new { success = false, message = "分享不存在" });

            var isFile = File.Exists(link.Path);
            var needPassword = !string.IsNullOrEmpty(link.Password);
            return Results.Ok(new
            {
                success = true,
                data = new
                {
                    link.Code,
                    link.Path,
                    link.MaxDownloads,
                    link.DownloadCount,
                    link.ExpiresAt,
                    NeedPassword = needPassword,
                    IsFile = isFile,
                    FileName = isFile ? Path.GetFileName(link.Path) : ""
                }
            });
        });

        app.MapPost("/api/shares/{code}/access", async (string code, HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFromJsonAsync<ShareAccessRequest>();
            var valid = await _shareService.ValidateShareAccessAsync(code, form?.Password);
            if (!valid) return Results.Ok(new { success = false, message = "访问失败，密码错误或分享已过期" });

            var link = await _shareService.GetShareLinkAsync(code);
            if (link == null) return Results.NotFound(new { success = false, message = "分享不存在" });

            if (string.IsNullOrWhiteSpace(link.Path))
                return Results.Ok(new { success = false, message = "分享路径无效" });

            if (File.Exists(link.Path))
            {
                var fileInfo = new FileInfo(link.Path);
                return Results.Ok(new
                {
                    success = true,
                    data = new
                    {
                        isFile = true,
                        fileName = fileInfo.Name,
                        fileSize = fileInfo.Length,
                        files = new object[0],
                        link.Path
                    }
                });
            }

            if (Directory.Exists(link.Path))
            {
                var files = _fileService.ListFiles(link.Path);
                return Results.Ok(new { success = true, data = new { isFile = false, files, link.Path } });
            }

            return Results.Ok(new { success = false, message = "分享路径不存在" });
        });

        app.MapGet("/api/shares/{code}/download", async (string code, string? path, HttpContext ctx) =>
        {
            var password = ctx.Request.Query.ContainsKey("password") ? ctx.Request.Query["password"].ToString() : null;

            var valid = await _shareService.ValidateShareAccessAsync(code, password);
            if (!valid) return Results.Ok(new { success = false, message = "访问失败" });

            var link = await _shareService.GetShareLinkAsync(code);
            if (link == null) return Results.NotFound(new { success = false, message = "分享不存在" });

            if (string.IsNullOrWhiteSpace(link.Path))
                return Results.NotFound(new { success = false, message = "分享路径无效" });

            string filePath;

            if (File.Exists(link.Path) && string.IsNullOrWhiteSpace(path))
            {
                filePath = link.Path;
            }
            else if (Directory.Exists(link.Path) && string.IsNullOrWhiteSpace(path))
            {
                filePath = link.Path;
            }
            else
            {
                var basePath = File.Exists(link.Path) ? Path.GetDirectoryName(link.Path)! : link.Path;
                filePath = Path.GetFullPath(Path.Combine(basePath, path ?? ""));
                var rootPath = Path.GetFullPath(link.Path);
                if (File.Exists(link.Path))
                    rootPath = Path.GetDirectoryName(link.Path)!;

                if (!filePath.StartsWith(Path.GetFullPath(rootPath), StringComparison.OrdinalIgnoreCase))
                    return Results.BadRequest(new { success = false, message = "非法路径" });
            }

            if (File.Exists(filePath))
            {
                await _shareService.IncrementDownloadCountAsync(link.Id);
                var fileInfo = new FileInfo(filePath);
                var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Results.File(stream, GetMimeType(fileInfo.Extension), fileInfo.Name);
            }

            if (Directory.Exists(filePath))
            {
                await _shareService.IncrementDownloadCountAsync(link.Id);
                var dirName = new DirectoryInfo(filePath).Name;
                var ms = new MemoryStream();
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
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

            return Results.NotFound(new { success = false, message = "文件不存在" });
        });

        app.MapDelete("/api/shares/{id}", async (int id, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var success = await _shareService.DeleteShareLinkAsync(id, userId);
            return Results.Ok(new { success, message = success ? "删除成功" : "删除失败" });
        }).RequireAuthorization();

        app.MapPut("/api/shares/{id}/disable", async (int id, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var success = await _shareService.DisableShareLinkAsync(id, userId);
            return Results.Ok(new { success, message = success ? "已禁用" : "操作失败" });
        }).RequireAuthorization();

        app.MapPut("/api/shares/{id}/enable", async (int id, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var success = await _shareService.EnableShareLinkAsync(id, userId);
            return Results.Ok(new { success, message = success ? "已启用" : "操作失败" });
        }).RequireAuthorization();
    }

    private static int GetUserId(HttpContext ctx)
    {
        var claim = ctx.User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null ? int.Parse(claim.Value) : 0;
    }

    private static string GetMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".mp4" => "video/mp4",
            ".mp3" => "audio/mpeg",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".zip" => "application/zip",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }
}

public record CreateShareRequest(int DirectoryId, string? Path, string? Password, DateTime? ExpiresAt, int MaxDownloads);
public record ShareAccessRequest(string? Password);
