using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using WinNASClient.Services;
using WinNASClient.Config;
using System.IO.Compression;

namespace WinNASClient.HttpServer.Controllers;

public class TeamController
{
    private readonly TeamService _teamService;
    private readonly SystemService _systemService;
    private readonly AuthService _authService;
    private readonly AppConfig _config;

    public TeamController(TeamService teamService, SystemService systemService, AuthService authService, AppConfig config)
    {
        _teamService = teamService;
        _systemService = systemService;
        _authService = authService;
        _config = config;
    }

    public void Register(WebApplication app)
    {
        #region Team CRUD

        app.MapGet("/api/teams", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var teams = await _teamService.GetUserTeamsAsync(userId);
            var result = new List<object>();
            foreach (var t in teams)
            {
                var creator = await _authService.GetUserByIdAsync(t.CreatorId);
                result.Add(new
                {
                    t.Id, t.Name, t.Description, t.Avatar, t.CreatorId, t.StoragePath, t.StorageLimit, t.CreatedAt, t.UpdatedAt,
                    CreatorName = creator?.Nickname ?? creator?.Username ?? "未知"
                });
            }
            return Results.Ok(new { success = true, data = result });
        }).RequireAuthorization();

        app.MapPost("/api/teams", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<CreateTeamRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var team = new Models.Team
            {
                Name = form.Name,
                Description = form.Description ?? "",
                CreatorId = userId,
                StoragePath = form.StoragePath ?? ""
            };

            var created = await _teamService.CreateTeamAsync(team);

            if (form.MemberIds != null && form.MemberIds.Length > 0)
            {
                foreach (var memberId in form.MemberIds)
                {
                    if (memberId != userId)
                        await _teamService.AddMemberAsync(created.Id, memberId, "member");
                }
            }

            return Results.Ok(new { success = true, data = created });
        }).RequireAuthorization();

        app.MapGet("/api/teams/{id}", async (int id, HttpContext ctx) =>
        {
            var team = await _teamService.GetTeamAsync(id);
            if (team == null) return Results.NotFound(new { success = false, message = "团队不存在" });
            var creator = await _authService.GetUserByIdAsync(team.CreatorId);
            return Results.Ok(new { success = true, data = new
            {
                team.Id, team.Name, team.Description, team.Avatar, team.CreatorId, team.StoragePath, team.StorageLimit, team.CreatedAt, team.UpdatedAt,
                CreatorName = creator?.Nickname ?? creator?.Username ?? "未知"
            } });
        }).RequireAuthorization();

        app.MapPut("/api/teams/{id}", async (int id, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var team = await _teamService.GetTeamAsync(id);
            if (team == null) return Results.Ok(new { success = false, message = "团队不存在" });
            if (team.CreatorId != userId) return Results.Ok(new { success = false, message = "没有权限" });

            var form = await ctx.Request.ReadFromJsonAsync<UpdateTeamRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            team.Name = form.Name ?? team.Name;
            team.Description = form.Description ?? team.Description;
            await _teamService.UpdateTeamAsync(team);

            return Results.Ok(new { success = true, message = "更新成功" });
        }).RequireAuthorization();

        app.MapDelete("/api/teams/{id}", async (int id, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var success = await _teamService.DeleteTeamAsync(id, userId);
            if (!success) return Results.Ok(new { success = false, message = "删除失败" });
            return Results.Ok(new { success = true, message = "删除成功" });
        }).RequireAuthorization();

        #endregion

        #region Team Members

        app.MapGet("/api/teams/{id}/members", async (int id, HttpContext ctx) =>
        {
            var members = await _teamService.GetTeamMembersAsync(id);
            return Results.Ok(new { success = true, data = members });
        }).RequireAuthorization();

        app.MapPost("/api/teams/{id}/members", async (int id, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<AddMemberRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var success = await _teamService.AddMemberAsync(id, form.UserId, form.Role);
            return Results.Ok(new { success, message = success ? "添加成功" : "添加失败" });
        }).RequireAuthorization();

        app.MapDelete("/api/teams/{teamId}/members/{userId}", async (int teamId, int userId, HttpContext ctx) =>
        {
            var success = await _teamService.RemoveMemberAsync(teamId, userId);
            return Results.Ok(new { success, message = success ? "移除成功" : "移除失败" });
        }).RequireAuthorization();

        app.MapPut("/api/teams/{teamId}/members/{userId}/role", async (int teamId, int userId, HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFromJsonAsync<UpdateRoleRequest>();
            if (form == null) return Results.Ok(new { success = false, message = "无效请求" });

            var success = await _teamService.UpdateMemberRoleAsync(teamId, userId, form.Role);
            return Results.Ok(new { success, message = success ? "更新成功" : "更新失败" });
        }).RequireAuthorization();

        #endregion

        #region Team Files

        app.MapGet("/api/teams/{teamId}/files", async (int teamId, string? path, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (!await _teamService.IsTeamMemberAsync(teamId, userId))
                return Results.Forbid();

            var files = await _teamService.GetTeamFilesAsync(teamId, path);
            return Results.Ok(new { success = true, data = files });
        }).RequireAuthorization();

        app.MapPost("/api/teams/{teamId}/files/upload", async (int teamId, string? path, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (!await _teamService.IsTeamMemberAsync(teamId, userId))
                return Results.Forbid();

            var form = await ctx.Request.ReadFormAsync();
            var results = new List<object>();

            foreach (var file in form.Files)
            {
                var tempPath = Path.GetTempFileName();
                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var fileName = file.FileName;
                var record = await _teamService.UploadTeamFileAsync(teamId, fileName, tempPath, userId, path);
                File.Delete(tempPath);
                results.Add(new { fileName = record.Name, success = true, version = record.Version });
            }

            await _systemService.LogOperationAsync(userId, "", "team_upload", teamId.ToString(), $"上传了{results.Count}个文件", ctx.Connection.RemoteIpAddress?.ToString() ?? "");

            return Results.Ok(new { success = true, data = results });
        }).RequireAuthorization();

        app.MapGet("/api/teams/{teamId}/files/{fileId}/download", async (int teamId, int fileId, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (!await _teamService.IsTeamMemberAsync(teamId, userId))
                return Results.Forbid();

            var file = await _teamService.GetTeamFileAsync(fileId);
            if (file == null)
                return Results.NotFound(new { success = false, message = "文件不存在" });

            if (file.MimeType == "folder")
            {
                if (!Directory.Exists(file.Path))
                    return Results.NotFound(new { success = false, message = "文件夹不存在" });

                var dirName = new DirectoryInfo(file.Path).Name;
                var ms = new MemoryStream();
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach (var f in Directory.GetFiles(file.Path, "*", SearchOption.AllDirectories))
                    {
                        var entryName = Path.GetRelativePath(file.Path, f);
                        archive.CreateEntryFromFile(f, entryName);
                    }
                }
                ms.Position = 0;
                return Results.File(ms, "application/zip", $"{dirName}.zip");
            }

            if (!File.Exists(file.Path))
                return Results.NotFound(new { success = false, message = "文件不存在" });

            var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Results.File(stream, file.MimeType, file.Name);
        }).RequireAuthorization();

        app.MapGet("/api/teams/{teamId}/files/{fileId}/stream", async (int teamId, int fileId, string? token, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == 0 && !string.IsNullOrEmpty(token))
            {
                var principal = Utils.JwtHelper.ValidateToken(token.Replace("Bearer ", ""), _config.JwtSecret);
                if (principal != null)
                    userId = int.Parse(principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
            }
            if (userId == 0) return Results.Unauthorized();

            if (!await _teamService.IsTeamMemberAsync(teamId, userId))
                return Results.Forbid();

            var file = await _teamService.GetTeamFileAsync(fileId);
            if (file == null || !File.Exists(file.Path))
                return Results.NotFound(new { success = false, message = "文件不存在" });

            var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Results.File(stream, file.MimeType, enableRangeProcessing: true);
        });

        app.MapGet("/api/teams/{teamId}/files/{fileId}/preview", async (int teamId, int fileId, string? token, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == 0 && !string.IsNullOrEmpty(token))
            {
                var principal = Utils.JwtHelper.ValidateToken(token.Replace("Bearer ", ""), _config.JwtSecret);
                if (principal != null)
                    userId = int.Parse(principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
            }
            if (userId == 0) return Results.Unauthorized();

            if (!await _teamService.IsTeamMemberAsync(teamId, userId))
                return Results.Forbid();

            var file = await _teamService.GetTeamFileAsync(fileId);
            if (file == null || !File.Exists(file.Path))
                return Results.NotFound(new { success = false, message = "文件不存在" });

            var ext = file.Extension?.ToLowerInvariant() ?? "";
            var isImage = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg" }.Contains(ext);
            var isText = new[] { ".txt", ".log", ".md", ".json", ".xml", ".csv", ".ini", ".cfg", ".conf", ".yml", ".yaml", ".html", ".css", ".js", ".ts", ".py", ".java", ".c", ".cpp", ".h", ".cs", ".go", ".rs", ".sh", ".bat", ".sql" }.Contains(ext);

            if (!isImage && !isText)
                return Results.Ok(new { success = false, message = "不支持预览此文件类型" });

            if (isImage)
            {
                var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Results.File(stream, file.MimeType);
            }

            var textContent = await File.ReadAllTextAsync(file.Path);
            return Results.Ok(new { success = true, data = new { content = textContent, name = file.Name, extension = ext } });
        });

        app.MapPost("/api/teams/{teamId}/files/folder", async (int teamId, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (!await _teamService.IsTeamMemberAsync(teamId, userId))
                return Results.Forbid();

            var form = await ctx.Request.ReadFromJsonAsync<CreateTeamFolderRequest>();
            if (form == null) return Results.BadRequest(new { success = false, message = "无效请求" });

            var folder = await _teamService.CreateTeamFolderAsync(teamId, form.Name, form.ParentPath, userId);
            return Results.Ok(new { success = true, data = folder });
        }).RequireAuthorization();

        app.MapPut("/api/teams/{teamId}/files/{fileId}/rename", async (int teamId, int fileId, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (!await _teamService.IsTeamMemberAsync(teamId, userId))
                return Results.Forbid();

            var form = await ctx.Request.ReadFromJsonAsync<RenameTeamFileRequest>();
            if (form == null) return Results.BadRequest(new { success = false, message = "无效请求" });

            var success = await _teamService.RenameTeamFileAsync(fileId, form.NewName, userId);
            return Results.Ok(new { success, message = success ? "重命名成功" : "重命名失败" });
        }).RequireAuthorization();

        app.MapDelete("/api/teams/{teamId}/files/{fileId}", async (int teamId, int fileId, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (!await _teamService.IsTeamMemberAsync(teamId, userId))
                return Results.Forbid();

            var success = await _teamService.DeleteTeamFileAsync(fileId, userId);
            return Results.Ok(new { success, message = success ? "删除成功" : "删除失败" });
        }).RequireAuthorization();

        app.MapPut("/api/teams/{teamId}/files/{fileId}/lock", async (int teamId, int fileId, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (!await _teamService.IsTeamMemberAsync(teamId, userId))
                return Results.Forbid();

            var success = await _teamService.LockTeamFileAsync(fileId, userId);
            return Results.Ok(new { success, message = success ? "锁定成功" : "锁定失败" });
        }).RequireAuthorization();

        app.MapPut("/api/teams/{teamId}/files/{fileId}/unlock", async (int teamId, int fileId, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (!await _teamService.IsTeamMemberAsync(teamId, userId))
                return Results.Forbid();

            var success = await _teamService.UnlockTeamFileAsync(fileId, userId);
            return Results.Ok(new { success, message = success ? "解锁成功" : "解锁失败" });
        }).RequireAuthorization();

        app.MapGet("/api/teams/{teamId}/files/{fileId}/versions", async (int teamId, int fileId, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (!await _teamService.IsTeamMemberAsync(teamId, userId))
                return Results.Forbid();

            var versions = await _teamService.GetFileVersionsAsync(fileId);
            return Results.Ok(new { success = true, data = versions });
        }).RequireAuthorization();

        #endregion

        #region Chat

        app.MapGet("/api/chat/history", async (int? teamId, int? receiverId, int limit, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var messages = await _teamService.GetChatHistoryAsync(teamId, receiverId, userId, limit);
            return Results.Ok(new { success = true, data = messages });
        }).RequireAuthorization();

        app.MapPost("/api/chat/send", async (HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            var form = await ctx.Request.ReadFromJsonAsync<SendMessageRequest>();
            if (form == null) return Results.BadRequest(new { success = false, message = "无效请求" });

            var msg = await _teamService.SendMessageAsync(userId, form.Content, form.ReceiverId, form.TeamId, form.MessageType ?? "text");
            return Results.Ok(new { success = true, data = msg });
        }).RequireAuthorization();

        #endregion
    }

    private static int GetUserId(HttpContext ctx)
    {
        var claim = ctx.User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null ? int.Parse(claim.Value) : 0;
    }

    private static string GetDocumentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".doc" or ".docx" or ".odt" or ".txt" or ".rtf" or ".html" or ".htm" or ".mht" => "word",
            ".xls" or ".xlsx" or ".ods" or ".csv" => "cell",
            ".ppt" or ".pptx" or ".odp" => "slide",
            ".pdf" => "word",
            _ => "word"
        };
    }
}

public record CreateTeamRequest(string Name, string? Description, string? StoragePath, int[]? MemberIds);
public record UpdateTeamRequest(string? Name, string? Description);
public record AddMemberRequest(int UserId, string Role = "member");
public record UpdateRoleRequest(string Role);
public record SendMessageRequest(string Content, int? ReceiverId, int? TeamId, string? MessageType);
public record CreateTeamFolderRequest(string Name, string? ParentPath);
public record RenameTeamFileRequest(string NewName);

