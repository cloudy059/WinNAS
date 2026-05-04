using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using WinNASClient.Config;
using WinNASClient.Services;
using WinNASClient.Utils;

namespace WinNASClient.HttpServer.Controllers;

public class NetworkShareController
{
    private readonly SmbService _smbService;
    private readonly FileService _fileService;
    private readonly AuthService _authService;
    private readonly AppConfig _config;

    public NetworkShareController(SmbService smbService, FileService fileService, AuthService authService, AppConfig config)
    {
        _smbService = smbService;
        _fileService = fileService;
        _authService = authService;
        _config = config;
    }

    public void Register(WebApplication app)
    {
        app.MapGet("/api/network-share/status", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null || user.Role != "admin")
                return Results.Ok(new { success = false, message = "需要管理员权限" });

            var directories = await _fileService.GetAllDirectoriesAsync();
            var dirPaths = directories.Select(d => d.Path);
            var smbShares = await _smbService.GetWinNASSharesAsync(dirPaths);
            var localIP = NetworkHelper.GetLocalIpAddress();

            var smbConfig = await _fileService.GetConfigAsync("smb_enabled");
            var smbEnabled = smbConfig?.ToLower() != "false";

            var webdavConfig = await _fileService.GetConfigAsync("webdav_enabled");
            var webdavEnabled = webdavConfig?.ToLower() != "false";

            return Results.Ok(new
            {
                success = true,
                data = new
                {
                    smbEnabled,
                    smbShares,
                    webdavEnabled,
                    webdavUrl = webdavEnabled ? $"http://{localIP}:{_config.HttpPort}/webdav/" : "",
                    smbPath = $"\\\\{localIP}",
                    localIP
                }
            });
        }).RequireAuthorization();

        app.MapPost("/api/network-share/toggle-smb/{id}", async (int id, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null || user.Role != "admin")
                return Results.Ok(new { success = false, message = "需要管理员权限" });

            var body = await ctx.Request.ReadFromJsonAsync<ToggleRequest>();
            if (body == null) return Results.Ok(new { success = false, message = "无效请求" });

            var dir = await _fileService.GetDirectoryAsync(id);
            if (dir == null) return Results.Ok(new { success = false, message = "目录不存在" });

            dir.AllowSmb = body.Enabled;
            await _fileService.UpdateDirectoryAsync(dir);

            if (body.Enabled)
            {
                var (ok, msg) = await _smbService.ShareDirectoryAsync(dir.Name, dir.Path, dir.Description);
                if (ok) await _fileService.SetConfigAsync("smb_enabled", "true");
                return Results.Ok(new { success = ok, message = ok ? $"SMB共享已启用: {dir.Name}" : msg });
            }
            else
            {
                var (ok, msg) = await _smbService.UnshareDirectoryAsync(dir.Name);
                if (ok)
                {
                    var remainingDirs = await _fileService.GetAllDirectoriesAsync();
                    var remaining = await _smbService.GetWinNASSharesAsync(remainingDirs.Where(d => d.AllowSmb).Select(d => d.Path));
                    if (remaining.Count == 0) await _fileService.SetConfigAsync("smb_enabled", "false");
                }
                return Results.Ok(new { success = ok, message = ok ? $"SMB共享已关闭: {dir.Name}" : msg });
            }
        }).RequireAuthorization();

        app.MapPost("/api/network-share/toggle-webdav/{id}", async (int id, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null || user.Role != "admin")
                return Results.Ok(new { success = false, message = "需要管理员权限" });

            var body = await ctx.Request.ReadFromJsonAsync<ToggleRequest>();
            if (body == null) return Results.Ok(new { success = false, message = "无效请求" });

            var dir = await _fileService.GetDirectoryAsync(id);
            if (dir == null) return Results.Ok(new { success = false, message = "目录不存在" });

            dir.AllowWebDav = body.Enabled;
            await _fileService.UpdateDirectoryAsync(dir);

            return Results.Ok(new { success = true, message = body.Enabled ? $"WebDAV共享已启用: {dir.Name}" : $"WebDAV共享已关闭: {dir.Name}" });
        }).RequireAuthorization();

        app.MapPost("/api/network-share/webdav/toggle", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null || user.Role != "admin")
                return Results.Ok(new { success = false, message = "需要管理员权限" });

            var body = await ctx.Request.ReadFromJsonAsync<ToggleRequest>();
            if (body == null) return Results.Ok(new { success = false, message = "无效请求" });

            await _fileService.SetConfigAsync("webdav_enabled", body.Enabled ? "true" : "false");

            return Results.Ok(new { success = true, message = body.Enabled ? "WebDAV服务已启用" : "WebDAV服务已关闭" });
        }).RequireAuthorization();

        app.MapPost("/api/network-share/smb/enable-all", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null || user.Role != "admin")
                return Results.Ok(new { success = false, message = "需要管理员权限" });

            var dirs = await _fileService.GetAllDirectoriesAsync();
            var messages = new List<string>();
            bool allOk = true;

            foreach (var dir in dirs.Where(d => d.AllowSmb))
            {
                var (ok, msg) = await _smbService.ShareDirectoryAsync(dir.Name, dir.Path, dir.Description);
                if (!ok) allOk = false;
                messages.Add(msg);
            }

            if (allOk && messages.Count > 0) await _fileService.SetConfigAsync("smb_enabled", "true");

            return Results.Ok(new { success = allOk, message = messages.Count > 0 ? string.Join("\n", messages) : "没有需要共享的目录" });
        }).RequireAuthorization();

        app.MapPost("/api/network-share/smb/disable-all", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null || user.Role != "admin")
                return Results.Ok(new { success = false, message = "需要管理员权限" });

            var (ok, msg) = await _smbService.UnshareAllAsync(await _fileService.GetAllDirectoriesAsync().ContinueWith(t => t.Result.Select(d => d.Path)));
            if (ok) await _fileService.SetConfigAsync("smb_enabled", "false");
            return Results.Ok(new { success = ok, message = msg });
        }).RequireAuthorization();
    }

    private static int GetUserId(HttpContext ctx)
    {
        var claim = ctx.User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null ? int.Parse(claim.Value) : 0;
    }
}

public class ToggleRequest
{
    public bool Enabled { get; set; }
}
