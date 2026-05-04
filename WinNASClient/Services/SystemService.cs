using WinNASClient.Database;
using WinNASClient.Models;
using WinNASClient.Utils;
using Dapper;
using System.Diagnostics;

namespace WinNASClient.Services;

public class SystemService
{
    private readonly DbRepository _repo;
    private static bool _restartRequested = false;

    public static bool IsRestartRequested => _restartRequested;

    public static void RequestRestart()
    {
        _restartRequested = true;
    }

    public static void ClearRestartRequest()
    {
        _restartRequested = false;
    }

    public SystemService(DbRepository repo)
    {
        _repo = repo;
    }

    public async Task LogOperationAsync(int userId, string username, string action, string target, string detail, string ipAddress)
    {
        await _repo.ExecuteAsync(@"
            INSERT INTO OperationLogs (UserId, Username, Action, Target, Detail, IpAddress)
            VALUES (@UserId, @Username, @Action, @Target, @Detail, @IpAddress)",
            new { UserId = userId, Username = username, Action = action, Target = target, Detail = detail, IpAddress = ipAddress });
    }

    public async Task<IEnumerable<OperationLog>> GetOperationLogsAsync(int page = 1, int pageSize = 20, string? action = null)
    {
        if (string.IsNullOrEmpty(action))
            return await _repo.QueryPagedAsync<OperationLog>("OperationLogs", page, pageSize, "1=1", "CreatedAt DESC");
        return await _repo.QueryPagedAsync<OperationLog>("OperationLogs", page, pageSize, "Action = @Action", "CreatedAt DESC", new { Action = action });
    }

    public async Task<string> GetConfigAsync(string key)
    {
        return await _repo.ExecuteScalarAsync<string>(
            "SELECT Value FROM SystemConfigs WHERE Key = @Key", new { Key = key }) ?? "";
    }

    public async Task<bool> SetConfigAsync(string key, string value)
    {
        return await _repo.ExecuteAsync(@"
            UPDATE SystemConfigs SET Value = @Value, UpdatedAt = datetime('now') WHERE Key = @Key",
            new { Key = key, Value = value }) > 0;
    }

    public async Task<IEnumerable<SystemConfig>> GetAllConfigsAsync()
    {
        return await _repo.QueryAsync<SystemConfig>("SELECT * FROM SystemConfigs ORDER BY Category, Key");
    }

    public async Task<Announcement> CreateAnnouncementAsync(Announcement announcement)
    {
        var id = await _repo.ExecuteScalarAsync<int>(@"
            INSERT INTO Announcements (Title, Content, CreatorId, IsPinned)
            VALUES (@Title, @Content, @CreatorId, @IsPinned);
            SELECT last_insert_rowid();", announcement);

        announcement.Id = id;
        return announcement;
    }

    public async Task<IEnumerable<Announcement>> GetAnnouncementsAsync()
    {
        return await _repo.QueryAsync<Announcement>(
            "SELECT * FROM Announcements ORDER BY IsPinned DESC, CreatedAt DESC");
    }

    public async Task<bool> DeleteAnnouncementAsync(int id)
    {
        return await _repo.ExecuteAsync("DELETE FROM Announcements WHERE Id = @Id", new { Id = id }) > 0;
    }

    public async Task<bool> UpdateAnnouncementAsync(int id, string title, string content, bool isPinned)
    {
        return await _repo.ExecuteAsync(@"
            UPDATE Announcements SET Title = @Title, Content = @Content, IsPinned = @IsPinned, UpdatedAt = @UpdatedAt
            WHERE Id = @Id",
            new { Id = id, Title = title, Content = content, IsPinned = isPinned, UpdatedAt = DateTime.UtcNow }) > 0;
    }

    public async Task RecordTrafficAsync(long bytesSent, long bytesReceived)
    {
        await _repo.ExecuteAsync(@"
            INSERT INTO NetworkTraffic (BytesSent, BytesReceived) VALUES (@Sent, @Received)",
            new { Sent = bytesSent, Received = bytesReceived });
    }

    public async Task<IEnumerable<NetworkTraffic>> GetTrafficHistoryAsync(int hours = 24)
    {
        return await _repo.QueryAsync<NetworkTraffic>(@"
            SELECT * FROM NetworkTraffic
            WHERE RecordedAt >= datetime('now', @Hours || ' hours')
            ORDER BY RecordedAt DESC",
            new { Hours = $"-{hours}" });
    }

    public List<NetworkInterfaceInfo> GetNetworkInterfaces()
    {
        return NetworkHelper.GetNetworkInterfaces();
    }

    public async Task<bool> SetAutoStartAsync(bool enable)
    {
        var appPath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(appPath)) return false;

        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

        if (key == null) return false;

        if (enable)
        {
            key.SetValue("WinNAS", $"\"{appPath}\"");
        }
        else
        {
            key.DeleteValue("WinNAS", false);
        }

        await SetConfigAsync("auto_start", enable.ToString().ToLower());
        return true;
    }

    public (long TotalMemory, long UsedMemory, long AvailableMemory) GetMemoryInfo()
    {
        var info = GC.GetGCMemoryInfo();
        var totalMemory = info.TotalAvailableMemoryBytes;
        var usedMemory = info.MemoryLoadBytes;
        return (totalMemory, usedMemory, totalMemory - usedMemory);
    }

    public (float CpuUsage, double DiskUsage, long DiskTotal, long DiskFree) GetSystemInfo()
    {
        var drive = new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory) ?? "C:\\");
        return (CpuMonitor.GetCpuUsage(), 
            Math.Round((double)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize * 100, 1),
            drive.TotalSize,
            drive.AvailableFreeSpace);
    }

    public async Task<bool> ResetSystemAsync()
    {
        await _repo.ExecuteAsync("DELETE FROM TeamFileVersions");
        await _repo.ExecuteAsync("DELETE FROM TeamFiles");
        await _repo.ExecuteAsync("DELETE FROM ChatMessages");
        await _repo.ExecuteAsync("DELETE FROM TeamMembers");
        await _repo.ExecuteAsync("DELETE FROM Teams");
        await _repo.ExecuteAsync("DELETE FROM ShareLinks");
        await _repo.ExecuteAsync("DELETE FROM FileRecords");
        await _repo.ExecuteAsync("DELETE FROM DirectoryPermissions");
        await _repo.ExecuteAsync("DELETE FROM SharedDirectories");
        await _repo.ExecuteAsync("DELETE FROM OperationLogs");
        await _repo.ExecuteAsync("DELETE FROM Announcements");
        await _repo.ExecuteAsync("DELETE FROM NetworkTraffic");
        await _repo.ExecuteAsync("DELETE FROM UserSessions");
        await _repo.ExecuteAsync("DELETE FROM Users WHERE Role != 'admin'");

        await _repo.ExecuteAsync("UPDATE SystemConfigs SET Value = 'true' WHERE Key = 'allow_register'");
        await _repo.ExecuteAsync("UPDATE SystemConfigs SET Value = 'true' WHERE Key = 'allow_guest'");
        await _repo.ExecuteAsync("UPDATE SystemConfigs SET Value = '8080' WHERE Key = 'http_port'");
        await _repo.ExecuteAsync("UPDATE SystemConfigs SET Value = '1073741824' WHERE Key = 'max_upload_size'");
        await _repo.ExecuteAsync("UPDATE SystemConfigs SET Value = 'true' WHERE Key = 'duplicate_check'");
        await _repo.ExecuteAsync("UPDATE SystemConfigs SET Value = 'false' WHERE Key = 'auto_start'");

        var salt = BCrypt.Net.BCrypt.GenerateSalt(12);
        var hash = BCrypt.Net.BCrypt.HashPassword("admin", salt);
        await _repo.ExecuteAsync("UPDATE Users SET PasswordHash = @Hash, Salt = @Salt, Nickname = 'Administrator' WHERE Role = 'admin'",
            new { Hash = hash, Salt = salt });

        return true;
    }
}
