using WinNASClient.Database;
using WinNASClient.Models;
using WinNASClient.Utils;
using Dapper;

namespace WinNASClient.Services;

public class TeamService
{
    private readonly DbRepository _repo;

    public TeamService(DbRepository repo)
    {
        _repo = repo;
    }

    #region Team CRUD

    public async Task<Team> CreateTeamAsync(Team team)
    {
        if (string.IsNullOrEmpty(team.StoragePath))
        {
            team.StoragePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WinNAS", "teams", $"team_{DateTime.Now:yyyyMMddHHmmss}");
        }

        if (!Directory.Exists(team.StoragePath))
            Directory.CreateDirectory(team.StoragePath);

        var id = await _repo.ExecuteScalarAsync<int>(@"
            INSERT INTO Teams (Name, Description, Avatar, CreatorId, StoragePath, StorageLimit)
            VALUES (@Name, @Description, @Avatar, @CreatorId, @StoragePath, @StorageLimit);
            SELECT last_insert_rowid();", team);

        team.Id = id;

        await _repo.ExecuteAsync(@"
            INSERT INTO TeamMembers (TeamId, UserId, Role) VALUES (@TeamId, @UserId, 'owner')",
            new { TeamId = id, UserId = team.CreatorId });

        return team;
    }

    public async Task<Team?> GetTeamAsync(int teamId)
    {
        return await _repo.QueryFirstOrDefaultAsync<Team>(
            "SELECT * FROM Teams WHERE Id = @Id", new { Id = teamId });
    }

    public async Task<IEnumerable<Team>> GetUserTeamsAsync(int userId)
    {
        return await _repo.QueryAsync<Team>(@"
            SELECT t.* FROM Teams t
            INNER JOIN TeamMembers tm ON t.Id = tm.TeamId
            WHERE tm.UserId = @UserId", new { UserId = userId });
    }

    public async Task<bool> UpdateTeamAsync(Team team)
    {
        return await _repo.ExecuteAsync(@"
            UPDATE Teams SET Name = @Name, Description = @Description,
            Avatar = @Avatar, StorageLimit = @StorageLimit, UpdatedAt = datetime('now')
            WHERE Id = @Id", team) > 0;
    }

    public async Task<bool> DeleteTeamAsync(int teamId, int userId)
    {
        var team = await GetTeamAsync(teamId);
        if (team == null || team.CreatorId != userId) return false;

        await _repo.ExecuteAsync("DELETE FROM TeamFiles WHERE TeamId = @Id", new { Id = teamId });
        await _repo.ExecuteAsync("DELETE FROM TeamMembers WHERE TeamId = @Id", new { Id = teamId });
        await _repo.ExecuteAsync("DELETE FROM ChatMessages WHERE TeamId = @Id", new { Id = teamId });
        return await _repo.ExecuteAsync("DELETE FROM Teams WHERE Id = @Id", new { Id = teamId }) > 0;
    }

    #endregion

    #region Team Members

    public async Task<bool> AddMemberAsync(int teamId, int userId, string role = "member")
    {
        var exists = await _repo.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM TeamMembers WHERE TeamId = @TeamId AND UserId = @UserId",
            new { TeamId = teamId, UserId = userId });
        if (exists > 0) return false;

        await _repo.ExecuteAsync(@"
            INSERT INTO TeamMembers (TeamId, UserId, Role) VALUES (@TeamId, @UserId, @Role)",
            new { TeamId = teamId, UserId = userId, Role = role });
        return true;
    }

    public async Task<bool> RemoveMemberAsync(int teamId, int userId)
    {
        var team = await GetTeamAsync(teamId);
        if (team == null) return false;
        if (team.CreatorId == userId) return false;

        return await _repo.ExecuteAsync(@"
            DELETE FROM TeamMembers WHERE TeamId = @TeamId AND UserId = @UserId",
            new { TeamId = teamId, UserId = userId }) > 0;
    }

    public async Task<bool> UpdateMemberRoleAsync(int teamId, int userId, string role)
    {
        return await _repo.ExecuteAsync(@"
            UPDATE TeamMembers SET Role = @Role WHERE TeamId = @TeamId AND UserId = @UserId",
            new { Role = role, TeamId = teamId, UserId = userId }) > 0;
    }

    public async Task<IEnumerable<TeamMember>> GetTeamMembersAsync(int teamId)
    {
        return await _repo.QueryAsync<TeamMember>(@"
            SELECT tm.*, u.Username, u.Nickname
            FROM TeamMembers tm
            LEFT JOIN Users u ON tm.UserId = u.Id
            WHERE tm.TeamId = @TeamId", new { TeamId = teamId });
    }

    public async Task<bool> IsTeamMemberAsync(int teamId, int userId)
    {
        var count = await _repo.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM TeamMembers WHERE TeamId = @TeamId AND UserId = @UserId",
            new { TeamId = teamId, UserId = userId });
        return count > 0;
    }

    #endregion

    #region Team Files

    public async Task<IEnumerable<TeamFile>> GetTeamFilesAsync(int teamId, string? directoryPath = null)
    {
        var sql = "SELECT * FROM TeamFiles WHERE TeamId = @TeamId";
        if (!string.IsNullOrEmpty(directoryPath))
            sql += " AND DirectoryPath = @DirectoryPath";
        else
            sql += " AND (DirectoryPath = '' OR DirectoryPath IS NULL)";

        return await _repo.QueryAsync<TeamFile>(sql, new { TeamId = teamId, DirectoryPath = directoryPath ?? "" });
    }

    public async Task<TeamFile?> GetTeamFileAsync(int fileId)
    {
        return await _repo.QueryFirstOrDefaultAsync<TeamFile>(
            "SELECT * FROM TeamFiles WHERE Id = @Id", new { Id = fileId });
    }

    public async Task<TeamFile> UploadTeamFileAsync(int teamId, string fileName, string tempFilePath, int ownerId, string? directoryPath = null, string? changeNote = null)
    {
        var team = await GetTeamAsync(teamId);
        if (team == null) throw new InvalidOperationException("团队不存在");

        var normalizedFileName = fileName.Replace('/', '\\');
        var subDir = Path.GetDirectoryName(normalizedFileName);
        var actualFileName = Path.GetFileName(normalizedFileName);

        var effectiveDirPath = string.IsNullOrEmpty(subDir)
            ? (directoryPath ?? "")
            : string.IsNullOrEmpty(directoryPath)
                ? subDir
                : Path.Combine(directoryPath, subDir);
        effectiveDirPath = effectiveDirPath.Replace('\\', '/');

        var targetDir = string.IsNullOrEmpty(effectiveDirPath) ? team.StoragePath : Path.Combine(team.StoragePath, effectiveDirPath);
        if (!Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        if (!string.IsNullOrEmpty(subDir))
        {
            await EnsureFolderRecordsAsync(teamId, team.StoragePath, effectiveDirPath, ownerId);
        }

        var targetPath = Path.Combine(targetDir, actualFileName);

        var existingFile = await _repo.QueryFirstOrDefaultAsync<TeamFile>(
            "SELECT * FROM TeamFiles WHERE TeamId = @TeamId AND Name = @Name AND DirectoryPath = @DirPath",
            new { TeamId = teamId, Name = actualFileName, DirPath = effectiveDirPath });

        if (existingFile != null)
        {
            var versionDir = Path.Combine(team.StoragePath, ".versions", existingFile.Id.ToString());
            if (!Directory.Exists(versionDir))
                Directory.CreateDirectory(versionDir);

            var versionPath = Path.Combine(versionDir, $"v{existingFile.Version}_{actualFileName}");
            if (File.Exists(existingFile.Path))
                File.Copy(existingFile.Path, versionPath, true);

            await _repo.ExecuteAsync(@"
                INSERT INTO TeamFileVersions (TeamFileId, Version, Path, Size, UploaderId, ChangeNote)
                VALUES (@FileId, @Version, @Path, @Size, @UploaderId, @ChangeNote)",
                new
                {
                    FileId = existingFile.Id,
                    Version = existingFile.Version,
                    Path = versionPath,
                    Size = existingFile.Size,
                    UploaderId = ownerId,
                    ChangeNote = changeNote ?? "文件更新"
                });

            File.Copy(tempFilePath, targetPath, true);
            var fileInfo = new FileInfo(targetPath);

            await _repo.ExecuteAsync(@"
                UPDATE TeamFiles SET Size = @Size, Md5 = @Md5, Version = Version + 1,
                OwnerId = @OwnerId, UpdatedAt = datetime('now') WHERE Id = @Id",
                new
                {
                    Size = fileInfo.Length,
                    Md5 = CryptoHelper.ComputeMd5(targetPath),
                    OwnerId = ownerId,
                    Id = existingFile.Id
                });

            existingFile.Size = fileInfo.Length;
            existingFile.Version++;
            existingFile.OwnerId = ownerId;
            return existingFile;
        }
        else
        {
            File.Copy(tempFilePath, targetPath, true);
            var fileInfo = new FileInfo(targetPath);

            var record = new TeamFile
            {
                TeamId = teamId,
                Name = actualFileName,
                Path = targetPath,
                DirectoryPath = effectiveDirPath,
                Size = fileInfo.Length,
                Extension = fileInfo.Extension.ToLowerInvariant(),
                MimeType = GetMimeType(fileInfo.Extension),
                Md5 = CryptoHelper.ComputeMd5(targetPath),
                OwnerId = ownerId
            };

            var id = await _repo.ExecuteScalarAsync<int>(@"
                INSERT INTO TeamFiles (TeamId, Name, Path, DirectoryPath, Size, Extension, MimeType, Md5, OwnerId)
                VALUES (@TeamId, @Name, @Path, @DirectoryPath, @Size, @Extension, @MimeType, @Md5, @OwnerId);
                SELECT last_insert_rowid();", record);

            record.Id = id;

            await _repo.ExecuteAsync(@"
                INSERT INTO TeamFileVersions (TeamFileId, Version, Path, Size, UploaderId, ChangeNote)
                VALUES (@FileId, 1, @Path, @Size, @UploaderId, @ChangeNote)",
                new { FileId = id, Path = targetPath, Size = fileInfo.Length, UploaderId = ownerId, ChangeNote = "初始版本" });

            return record;
        }
    }

    private async Task EnsureFolderRecordsAsync(int teamId, string storagePath, string effectiveDirPath, int ownerId)
    {
        var parts = effectiveDirPath.Split('/', '\\');
        var currentPath = "";
        for (var i = 0; i < parts.Length; i++)
        {
            if (string.IsNullOrEmpty(parts[i])) continue;
            var parentPath = currentPath;
            currentPath = string.IsNullOrEmpty(currentPath) ? parts[i] : $"{currentPath}/{parts[i]}";

            var exists = await _repo.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM TeamFiles WHERE TeamId = @TeamId AND Name = @Name AND DirectoryPath = @DirPath AND MimeType = 'folder'",
                new { TeamId = teamId, Name = parts[i], DirPath = parentPath });
            if (exists > 0) continue;

            var fullPath = Path.Combine(storagePath, currentPath);
            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);

            var record = new TeamFile
            {
                TeamId = teamId,
                Name = parts[i],
                Path = fullPath,
                DirectoryPath = parentPath,
                Extension = "",
                MimeType = "folder",
                OwnerId = ownerId
            };

            await _repo.ExecuteScalarAsync<int>(@"
                INSERT INTO TeamFiles (TeamId, Name, Path, DirectoryPath, Extension, MimeType, OwnerId)
                VALUES (@TeamId, @Name, @Path, @DirectoryPath, @Extension, @MimeType, @OwnerId);
                SELECT last_insert_rowid();", record);
        }
    }

    public async Task<bool> DeleteTeamFileAsync(int fileId, int userId)
    {
        var file = await GetTeamFileAsync(fileId);
        if (file == null) return false;

        if (File.Exists(file.Path))
            File.Delete(file.Path);

        await _repo.ExecuteAsync("DELETE FROM TeamFileVersions WHERE TeamFileId = @Id", new { Id = fileId });
        return await _repo.ExecuteAsync("DELETE FROM TeamFiles WHERE Id = @Id", new { Id = fileId }) > 0;
    }

    public async Task<bool> RenameTeamFileAsync(int fileId, string newName, int userId)
    {
        var file = await GetTeamFileAsync(fileId);
        if (file == null) return false;

        var newPath = Path.Combine(Path.GetDirectoryName(file.Path)!, newName);
        if (File.Exists(file.Path))
            File.Move(file.Path, newPath);
        else if (Directory.Exists(file.Path))
            Directory.Move(file.Path, newPath);

        return await _repo.ExecuteAsync(@"
            UPDATE TeamFiles SET Name = @Name, Path = @NewPath, UpdatedAt = datetime('now')
            WHERE Id = @Id", new { Name = newName, NewPath = newPath, Id = fileId }) > 0;
    }

    public async Task<bool> LockTeamFileAsync(int fileId, int userId)
    {
        return await _repo.ExecuteAsync(@"
            UPDATE TeamFiles SET IsLocked = 1, LockedBy = @UserId, UpdatedAt = datetime('now')
            WHERE Id = @Id AND (IsLocked = 0 OR LockedBy = @UserId)",
            new { UserId = userId, Id = fileId }) > 0;
    }

    public async Task<bool> UnlockTeamFileAsync(int fileId, int userId)
    {
        return await _repo.ExecuteAsync(@"
            UPDATE TeamFiles SET IsLocked = 0, LockedBy = NULL, UpdatedAt = datetime('now')
            WHERE Id = @Id AND LockedBy = @UserId",
            new { UserId = userId, Id = fileId }) > 0;
    }

    public async Task<IEnumerable<TeamFileVersion>> GetFileVersionsAsync(int fileId)
    {
        return await _repo.QueryAsync<TeamFileVersion>(
            "SELECT * FROM TeamFileVersions WHERE TeamFileId = @FileId ORDER BY Version DESC",
            new { FileId = fileId });
    }

    public async Task<TeamFile> CreateTeamFolderAsync(int teamId, string folderName, string? parentPath = null, int ownerId = 0)
    {
        var team = await GetTeamAsync(teamId);
        if (team == null) throw new InvalidOperationException("团队不存在");

        var fullPath = string.IsNullOrEmpty(parentPath)
            ? Path.Combine(team.StoragePath, folderName)
            : Path.Combine(team.StoragePath, parentPath, folderName);

        if (!Directory.Exists(fullPath))
            Directory.CreateDirectory(fullPath);

        var record = new TeamFile
        {
            TeamId = teamId,
            Name = folderName,
            Path = fullPath,
            DirectoryPath = parentPath ?? "",
            Extension = "",
            MimeType = "folder",
            OwnerId = ownerId
        };

        var id = await _repo.ExecuteScalarAsync<int>(@"
            INSERT INTO TeamFiles (TeamId, Name, Path, DirectoryPath, Extension, MimeType, OwnerId)
            VALUES (@TeamId, @Name, @Path, @DirectoryPath, @Extension, @MimeType, @OwnerId);
            SELECT last_insert_rowid();", record);

        record.Id = id;
        return record;
    }

    #endregion

    #region Chat

    public async Task<ChatMessage> SendMessageAsync(int senderId, string content, int? receiverId = null, int? teamId = null, string messageType = "text")
    {
        var msg = new ChatMessage
        {
            SenderId = senderId,
            ReceiverId = receiverId,
            TeamId = teamId,
            Content = content,
            MessageType = messageType
        };

        var id = await _repo.ExecuteScalarAsync<int>(@"
            INSERT INTO ChatMessages (SenderId, ReceiverId, TeamId, Content, MessageType)
            VALUES (@SenderId, @ReceiverId, @TeamId, @Content, @MessageType);
            SELECT last_insert_rowid();", msg);

        msg.Id = id;

        var sender = await _repo.QueryFirstOrDefaultAsync<dynamic>("SELECT Nickname, Username FROM Users WHERE Id = @Id", new { Id = senderId });
        msg.SenderName = sender?.Nickname ?? sender?.Username;

        if (receiverId.HasValue)
        {
            var receiver = await _repo.QueryFirstOrDefaultAsync<dynamic>("SELECT Nickname FROM Users WHERE Id = @Id", new { Id = receiverId.Value });
            msg.ReceiverName = receiver?.Nickname;
        }

        return msg;
    }

    public async Task<IEnumerable<ChatMessage>> GetChatHistoryAsync(int? teamId, int? receiverId, int userId, int limit = 50)
    {
        if (teamId.HasValue)
        {
            return await _repo.QueryAsync<ChatMessage>(@"
                SELECT c.*, u.Nickname AS SenderName
                FROM ChatMessages c
                LEFT JOIN Users u ON c.SenderId = u.Id
                WHERE c.TeamId = @TeamId
                ORDER BY c.CreatedAt DESC LIMIT @Limit", new { TeamId = teamId, Limit = limit });
        }

        if (receiverId.HasValue)
        {
            return await _repo.QueryAsync<ChatMessage>(@"
                SELECT c.*, s.Nickname AS SenderName, r.Nickname AS ReceiverName
                FROM ChatMessages c
                LEFT JOIN Users s ON c.SenderId = s.Id
                LEFT JOIN Users r ON c.ReceiverId = r.Id
                WHERE (c.SenderId = @UserId AND c.ReceiverId = @ReceiverId)
                   OR (c.SenderId = @ReceiverId AND c.ReceiverId = @UserId)
                ORDER BY c.CreatedAt DESC LIMIT @Limit",
                new { UserId = userId, ReceiverId = receiverId, Limit = limit });
        }

        return Enumerable.Empty<ChatMessage>();
    }

    #endregion

    private static string GetMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls" => "application/vnd.ms-excel",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }
}
