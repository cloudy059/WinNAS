using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using WinNASClient.Config;
using WinNASClient.Services;
using WinNASClient.Utils;

namespace WinNASClient.HttpServer.Controllers;

public class SystemController
{
    private readonly SystemService _systemService;
    private readonly FileService _fileService;
    private readonly ShareService _shareService;
    private readonly TeamService _teamService;
    private readonly AuthService _authService;
    private readonly AppConfig _config;

    public SystemController(SystemService systemService, FileService fileService, ShareService shareService, TeamService teamService, AuthService authService, AppConfig config)
    {
        _systemService = systemService;
        _fileService = fileService;
        _shareService = shareService;
        _teamService = teamService;
        _authService = authService;
        _config = config;
    }

    public void Register(WebApplication app)
    {
        app.MapGet("/api/server/address", () =>
        {
            var ip = NetworkHelper.GetLocalIpAddress();
            return Results.Ok(new { success = true, data = new { Ip = ip, Port = _config.HttpPort, Url = $"http://{ip}:{_config.HttpPort}" } });
        });

        app.MapGet("/api/system/info", (HttpContext ctx) =>
        {
            var (cpu, diskUsage, diskTotal, diskFree) = _systemService.GetSystemInfo();
            var (totalMem, usedMem, availMem) = _systemService.GetMemoryInfo();
            var networks = _systemService.GetNetworkInterfaces();

            return Results.Ok(new
            {
                success = true,
                data = new
                {
                    CpuUsage = cpu,
                    DiskUsage = diskUsage,
                    DiskTotal = diskTotal,
                    DiskFree = diskFree,
                    TotalMemory = totalMem,
                    UsedMemory = usedMem,
                    AvailableMemory = availMem,
                    Networks = networks,
                    HttpPort = _config.HttpPort,
                    AllowRegister = _config.AllowRegister,
                    AllowGuest = _config.AllowGuest
                }
            });
        }).RequireAuthorization();

        app.MapGet("/api/system/networks", () =>
        {
            var networks = _systemService.GetNetworkInterfaces();
            return Results.Ok(new { success = true, data = networks });
        }).RequireAuthorization();

        app.MapGet("/api/system/configs", async (HttpContext ctx) =>
        {
            var configs = await _systemService.GetAllConfigsAsync();
            return Results.Ok(new { success = true, data = configs });
        }).RequireAuthorization();

        app.MapPut("/api/system/configs", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFromJsonAsync<UpdateConfigRequest>();
            if (form == null) return Results.BadRequest(new { success = false, message = "无效请求" });

            var success = await _systemService.SetConfigAsync(form.Key, form.Value);
            if (success)
            {
                switch (form.Key)
                {
                    case "http_port": if (int.TryParse(form.Value, out int port)) _config.HttpPort = port; break;
                    case "allow_register": _config.AllowRegister = form.Value?.ToLower() != "false"; break;
                    case "allow_guest": _config.AllowGuest = form.Value?.ToLower() != "false"; break;
                    case "duplicate_check": _config.DuplicateCheck = form.Value?.ToLower() != "false"; break;
                    case "max_upload_size": if (long.TryParse(form.Value, out long size)) _config.MaxUploadSize = size; break;
                }
            }
            return Results.Ok(new { success, message = success ? "更新成功" : "更新失败" });
        }).RequireAuthorization();

        app.MapGet("/api/system/logs", async (int page, int pageSize, string? action, HttpContext ctx) =>
        {
            var logs = await _systemService.GetOperationLogsAsync(page, pageSize, action);
            return Results.Ok(new { success = true, data = logs });
        }).RequireAuthorization();

        app.MapGet("/api/system/traffic", async (int hours, HttpContext ctx) =>
        {
            var traffic = await _systemService.GetTrafficHistoryAsync(hours);
            return Results.Ok(new { success = true, data = traffic });
        }).RequireAuthorization();

        app.MapPost("/api/system/autostart", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFromJsonAsync<SetAutoStartRequest>();
            if (form == null) return Results.BadRequest(new { success = false, message = "无效请求" });

            var success = await _systemService.SetAutoStartAsync(form.Enable);
            if (success) _config.AutoStart = form.Enable;
            return Results.Ok(new { success, message = success ? "设置成功" : "设置失败" });
        }).RequireAuthorization();

        app.MapGet("/api/announcements", async () =>
        {
            var announcements = await _systemService.GetAnnouncementsAsync();
            return Results.Ok(new { success = true, data = announcements });
        });

        app.MapPost("/api/announcements", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFromJsonAsync<CreateAnnouncementRequest>();
            if (form == null) return Results.BadRequest(new { success = false, message = "无效请求" });

            var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
            var announcement = await _systemService.CreateAnnouncementAsync(new Models.Announcement
            {
                Title = form.Title,
                Content = form.Content,
                CreatorId = userId,
                IsPinned = form.IsPinned
            });

            return Results.Ok(new { success = true, data = announcement });
        }).RequireAuthorization();

        app.MapDelete("/api/announcements/{id}", async (int id) =>
        {
            var success = await _systemService.DeleteAnnouncementAsync(id);
            return Results.Ok(new { success, message = success ? "删除成功" : "删除失败" });
        }).RequireAuthorization();

        app.MapPut("/api/announcements/{id}", async (int id, HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFromJsonAsync<UpdateAnnouncementRequest>();
            if (form == null) return Results.BadRequest(new { success = false, message = "无效请求" });

            var success = await _systemService.UpdateAnnouncementAsync(id, form.Title, form.Content, form.IsPinned);
            return Results.Ok(new { success, message = success ? "更新成功" : "更新失败" });
        }).RequireAuthorization();

        app.MapGet("/api/system/stats", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);

            var dirs = await _fileService.GetSharedDirectoriesAsync(userId);
            var dirCount = dirs.Count();

            var shares = await _shareService.GetShareLinksByUserAsync(userId);
            var shareCount = shares.Count();

            var teams = await _teamService.GetUserTeamsAsync(userId);
            var teamCount = teams.Count();

            var users = await _authService.GetAllUsersAsync();
            var userCount = users.Count();

            var sysInfo = _systemService.GetSystemInfo();

            return Results.Ok(new
            {
                success = true,
                data = new
                {
                    directoryCount = dirCount,
                    shareCount = shareCount,
                    teamCount = teamCount,
                    userCount = userCount,
                    cpuUsage = sysInfo.CpuUsage,
                    diskUsage = sysInfo.DiskUsage,
                    diskTotal = sysInfo.DiskTotal,
                    diskFree = sysInfo.DiskFree
                }
            });
        }).RequireAuthorization();

        app.MapPost("/api/system/reset", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null || user.Role != "admin")
                return Results.Ok(new { success = false, message = "仅管理员可执行初始化操作" });

            var form = await ctx.Request.ReadFromJsonAsync<ResetSystemRequest>();
            if (form == null || form.ConfirmCode != "RESET")
                return Results.Ok(new { success = false, message = "确认码错误" });

            var success = await _systemService.ResetSystemAsync();
            return Results.Ok(new { success, message = success ? "系统已初始化，请重新登录" : "初始化失败" });
        }).RequireAuthorization();

        app.MapPost("/api/system/restart", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null || user.Role != "admin")
                return Results.Ok(new { success = false, message = "仅管理员可重启服务" });

            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                SystemService.RequestRestart();
            });

            return Results.Ok(new { success = true, message = "服务正在重启..." });
        }).RequireAuthorization();
    }

    private static int GetUserId(HttpContext ctx)
    {
        var claim = ctx.User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null ? int.Parse(claim.Value) : 0;
    }
}

public record UpdateConfigRequest(string Key, string Value);
public record SetAutoStartRequest(bool Enable);
public record CreateAnnouncementRequest(string Title, string Content, bool IsPinned = false);
public record UpdateAnnouncementRequest(string Title, string Content, bool IsPinned = false);
public record ResetSystemRequest(string ConfirmCode);
