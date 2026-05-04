using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using WinNASClient.Config;
using WinNASClient.Services;

namespace WinNASClient.HttpServer.Controllers;

public class AuthController
{
    private readonly AuthService _authService;
    private readonly SystemService _systemService;
    private readonly AppConfig _config;

    public AuthController(AuthService authService, SystemService systemService, AppConfig config)
    {
        _authService = authService;
        _systemService = systemService;
        _config = config;
    }

    public void Register(WebApplication app)
    {
        app.MapPost("/api/auth/login", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFromJsonAsync<LoginRequest>();
            if (form == null) return Results.BadRequest(new { success = false, message = "无效请求" });

            var (success, message, token, user) = await _authService.LoginAsync(
                form.Username, form.Password,
                ctx.Connection.RemoteIpAddress?.ToString() ?? "",
                ctx.Request.Headers.UserAgent.ToString());

            if (!success) return Results.Ok(new { success = false, message });

            await _systemService.LogOperationAsync(user!.Id, user.Username, "login", "auth", "用户登录", ctx.Connection.RemoteIpAddress?.ToString() ?? "");

            return Results.Ok(new
            {
                success = true,
                message,
                data = new
                {
                    token,
                    user = new { user!.Id, user.Username, user.Nickname, user.Avatar, user.Role }
                }
            });
        });

        app.MapPost("/api/auth/register", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFromJsonAsync<RegisterRequest>();
            if (form == null) return Results.BadRequest(new { success = false, message = "无效请求" });

            var (success, message, user) = await _authService.RegisterAsync(
                form.Username, form.Password, form.Nickname, _config.AllowRegister);

            if (!success) return Results.Ok(new { success = false, message });

            return Results.Ok(new { success = true, message, data = new { user!.Id, user!.Username, user!.Nickname } });
        });

        app.MapPost("/api/auth/logout", async (HttpContext ctx) =>
        {
            var token = ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            await _authService.LogoutAsync(token);
            return Results.Ok(new { success = true, message = "已退出登录" });
        }).RequireAuthorization();

        app.MapGet("/api/auth/profile", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null) return Results.NotFound(new { success = false, message = "用户不存在" });

            return Results.Ok(new
            {
                success = true,
                data = new { user.Id, user.Username, user.Nickname, user.Avatar, user.Role, user.StorageLimit, user.IsEnabled }
            });
        }).RequireAuthorization();

        app.MapPut("/api/auth/profile", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<UpdateProfileRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null) return Results.Ok(new { success = false, message = "用户不存在" });

            user.Nickname = form.Nickname ?? user.Nickname;
            user.Avatar = form.Avatar ?? user.Avatar;

            if (!string.IsNullOrWhiteSpace(form.Password))
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(form.Password);
            }

            await _authService.UpdateUserAsync(user);

            return Results.Ok(new
            {
                success = true,
                message = "更新成功",
                data = new { id = user.Id, username = user.Username, nickname = user.Nickname, role = user.Role }
            });
        }).RequireAuthorization();

        app.MapPut("/api/auth/password", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<ChangePasswordRequest>();
            if (form == null) return Results.BadRequest(new { success = false, message = "无效请求" });

            var success = await _authService.ChangePasswordAsync(userId, form.OldPassword, form.NewPassword);
            if (!success) return Results.Ok(new { success = false, message = "原密码错误" });

            return Results.Ok(new { success = true, message = "密码修改成功" });
        }).RequireAuthorization();

        app.MapGet("/api/users", async (HttpContext ctx) =>
        {
            var user = await _authService.GetUserByIdAsync(GetUserId(ctx));
            if (user?.Role != "admin") return Results.Forbid();

            var users = await _authService.GetAllUsersAsync();
            return Results.Ok(new { success = true, data = users });
        }).RequireAuthorization();

        app.MapGet("/api/users/list", async (HttpContext ctx) =>
        {
            var users = await _authService.GetAllUsersAsync();
            return Results.Ok(new { success = true, data = users.Select(u => new { u.Id, u.Username, u.Nickname, u.Role }) });
        }).RequireAuthorization();

        app.MapPut("/api/users/{id}", async (int id, HttpContext ctx) =>
        {
            var currentUser = await _authService.GetUserByIdAsync(GetUserId(ctx));
            if (currentUser?.Role != "admin") return Results.Forbid();

            var form = await ctx.Request.ReadFromJsonAsync<AdminUpdateUserRequest>();
            if (form == null) return Results.BadRequest(new { success = false, message = "无效请求" });

            var target = await _authService.GetUserByIdAsync(id);
            if (target == null) return Results.NotFound(new { success = false, message = "用户不存在" });

            target.Role = form.Role ?? target.Role;
            target.IsEnabled = form.IsEnabled ?? target.IsEnabled;
            target.StorageLimit = form.StorageLimit ?? target.StorageLimit;
            await _authService.UpdateUserAsync(target);

            return Results.Ok(new { success = true, message = "更新成功" });
        }).RequireAuthorization();

        app.MapDelete("/api/users/{id}", async (int id, HttpContext ctx) =>
        {
            var currentUser = await _authService.GetUserByIdAsync(GetUserId(ctx));
            if (currentUser?.Role != "admin") return Results.Forbid();
            if (id == currentUser.Id) return Results.Ok(new { success = false, message = "不能删除自己" });

            await _authService.DeleteUserAsync(id);
            return Results.Ok(new { success = true, message = "删除成功" });
        }).RequireAuthorization();

        app.MapGet("/api/auth/guest", () =>
        {
            return Results.Ok(new
            {
                success = _config.AllowGuest,
                message = _config.AllowGuest ? "访客模式已开启" : "访客模式未开启"
            });
        });
    }

    private static int GetUserId(HttpContext ctx)
    {
        var claim = ctx.User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null ? int.Parse(claim.Value) : 0;
    }
}

public record LoginRequest(string Username, string Password);
public record RegisterRequest(string Username, string Password, string? Nickname);
public record UpdateProfileRequest(string? Nickname, string? Avatar, string? Password);
public record ChangePasswordRequest(string OldPassword, string NewPassword);
public record AdminUpdateUserRequest(string? Role, bool? IsEnabled, int? StorageLimit);
