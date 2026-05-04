using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;

namespace WinNASClient.Database;

public class DbInitializer
{
    private readonly string _connectionString;

    public DbInitializer(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    public string ConnectionString => _connectionString;

    public SqliteConnection CreateConnection() => new SqliteConnection(_connectionString);

    public async Task InitializeAsync()
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                Salt TEXT NOT NULL,
                Nickname TEXT NOT NULL DEFAULT '',
                Avatar TEXT NOT NULL DEFAULT '',
                Role TEXT NOT NULL DEFAULT 'user',
                StorageLimit INTEGER NOT NULL DEFAULT -1,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                AllowRegister INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS UserSessions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                Token TEXT NOT NULL UNIQUE,
                DeviceInfo TEXT NOT NULL DEFAULT '',
                IpAddress TEXT NOT NULL DEFAULT '',
                ExpiresAt TEXT NOT NULL,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (UserId) REFERENCES Users(Id)
            );

            CREATE TABLE IF NOT EXISTS SharedDirectories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Path TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                OwnerId INTEGER NOT NULL,
                Visibility TEXT NOT NULL DEFAULT 'private',
                AllowUpload INTEGER NOT NULL DEFAULT 1,
                AllowDelete INTEGER NOT NULL DEFAULT 0,
                AllowRename INTEGER NOT NULL DEFAULT 0,
                AllowMove INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (OwnerId) REFERENCES Users(Id)
            );

            CREATE TABLE IF NOT EXISTS DirectoryPermissions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DirectoryId INTEGER NOT NULL,
                UserId INTEGER NOT NULL,
                CanRead INTEGER NOT NULL DEFAULT 1,
                CanWrite INTEGER NOT NULL DEFAULT 0,
                CanDelete INTEGER NOT NULL DEFAULT 0,
                CanRename INTEGER NOT NULL DEFAULT 0,
                CanMove INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (DirectoryId) REFERENCES SharedDirectories(Id),
                FOREIGN KEY (UserId) REFERENCES Users(Id)
            );

            CREATE TABLE IF NOT EXISTS FileRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Path TEXT NOT NULL,
                DirectoryPath TEXT NOT NULL,
                DirectoryId INTEGER NOT NULL,
                Size INTEGER NOT NULL DEFAULT 0,
                Extension TEXT NOT NULL DEFAULT '',
                MimeType TEXT NOT NULL DEFAULT '',
                Md5 TEXT NOT NULL DEFAULT '',
                OwnerId INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (DirectoryId) REFERENCES SharedDirectories(Id),
                FOREIGN KEY (OwnerId) REFERENCES Users(Id)
            );

            CREATE TABLE IF NOT EXISTS ShareLinks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Code TEXT NOT NULL UNIQUE,
                Path TEXT NOT NULL,
                DirectoryId INTEGER NOT NULL,
                CreatorId INTEGER NOT NULL,
                Password TEXT NOT NULL DEFAULT '',
                ExpiresAt TEXT,
                MaxDownloads INTEGER NOT NULL DEFAULT -1,
                DownloadCount INTEGER NOT NULL DEFAULT 0,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (DirectoryId) REFERENCES SharedDirectories(Id),
                FOREIGN KEY (CreatorId) REFERENCES Users(Id)
            );

            CREATE TABLE IF NOT EXISTS Teams (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                Avatar TEXT NOT NULL DEFAULT '',
                CreatorId INTEGER NOT NULL,
                StoragePath TEXT NOT NULL DEFAULT '',
                StorageLimit INTEGER NOT NULL DEFAULT -1,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (CreatorId) REFERENCES Users(Id)
            );

            CREATE TABLE IF NOT EXISTS TeamMembers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TeamId INTEGER NOT NULL,
                UserId INTEGER NOT NULL,
                Role TEXT NOT NULL DEFAULT 'member',
                JoinedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (TeamId) REFERENCES Teams(Id),
                FOREIGN KEY (UserId) REFERENCES Users(Id)
            );

            CREATE TABLE IF NOT EXISTS ChatMessages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SenderId INTEGER NOT NULL,
                ReceiverId INTEGER,
                TeamId INTEGER,
                Content TEXT NOT NULL,
                MessageType TEXT NOT NULL DEFAULT 'text',
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (SenderId) REFERENCES Users(Id),
                FOREIGN KEY (ReceiverId) REFERENCES Users(Id),
                FOREIGN KEY (TeamId) REFERENCES Teams(Id)
            );

            CREATE TABLE IF NOT EXISTS OperationLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                Username TEXT NOT NULL DEFAULT '',
                Action TEXT NOT NULL,
                Target TEXT NOT NULL DEFAULT '',
                Detail TEXT NOT NULL DEFAULT '',
                IpAddress TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS SystemConfigs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Key TEXT NOT NULL UNIQUE,
                Value TEXT NOT NULL DEFAULT '',
                Category TEXT NOT NULL DEFAULT '',
                Description TEXT NOT NULL DEFAULT '',
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS Announcements (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Content TEXT NOT NULL,
                CreatorId INTEGER NOT NULL,
                IsPinned INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (CreatorId) REFERENCES Users(Id)
            );

            CREATE TABLE IF NOT EXISTS NetworkTraffic (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BytesSent INTEGER NOT NULL DEFAULT 0,
                BytesReceived INTEGER NOT NULL DEFAULT 0,
                RecordedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS TeamFiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TeamId INTEGER NOT NULL,
                Name TEXT NOT NULL,
                Path TEXT NOT NULL,
                DirectoryPath TEXT NOT NULL DEFAULT '',
                Size INTEGER NOT NULL DEFAULT 0,
                Extension TEXT NOT NULL DEFAULT '',
                MimeType TEXT NOT NULL DEFAULT '',
                Md5 TEXT NOT NULL DEFAULT '',
                OwnerId INTEGER NOT NULL,
                IsLocked INTEGER NOT NULL DEFAULT 0,
                LockedBy INTEGER,
                Version INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (TeamId) REFERENCES Teams(Id),
                FOREIGN KEY (OwnerId) REFERENCES Users(Id)
            );

            CREATE TABLE IF NOT EXISTS TeamFileVersions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TeamFileId INTEGER NOT NULL,
                Version INTEGER NOT NULL,
                Path TEXT NOT NULL,
                Size INTEGER NOT NULL DEFAULT 0,
                UploaderId INTEGER NOT NULL,
                ChangeNote TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (TeamFileId) REFERENCES TeamFiles(Id),
                FOREIGN KEY (UploaderId) REFERENCES Users(Id)
            );

            CREATE TABLE IF NOT EXISTS FileLocks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DirectoryId INTEGER NOT NULL,
                FilePath TEXT NOT NULL,
                UserId INTEGER NOT NULL,
                Username TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                ExpiresAt TEXT,
                FOREIGN KEY (DirectoryId) REFERENCES SharedDirectories(Id),
                FOREIGN KEY (UserId) REFERENCES Users(Id)
            );

            CREATE TABLE IF NOT EXISTS RecycleBin (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DirectoryId INTEGER NOT NULL,
                FileName TEXT NOT NULL,
                OriginalPath TEXT NOT NULL,
                RecycledPath TEXT NOT NULL,
                IsDirectory INTEGER NOT NULL DEFAULT 0,
                Size INTEGER NOT NULL DEFAULT 0,
                UserId INTEGER NOT NULL,
                Username TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (DirectoryId) REFERENCES SharedDirectories(Id),
                FOREIGN KEY (UserId) REFERENCES Users(Id)
            );

            CREATE TABLE IF NOT EXISTS Favorites (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                DirectoryId INTEGER NOT NULL,
                FilePath TEXT NOT NULL DEFAULT '',
                FileName TEXT NOT NULL,
                IsDirectory INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (UserId) REFERENCES Users(Id),
                FOREIGN KEY (DirectoryId) REFERENCES SharedDirectories(Id)
            );

            CREATE TABLE IF NOT EXISTS RecentAccess (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                DirectoryId INTEGER NOT NULL,
                FilePath TEXT NOT NULL DEFAULT '',
                FileName TEXT NOT NULL,
                AccessType TEXT NOT NULL DEFAULT 'browse',
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (UserId) REFERENCES Users(Id),
                FOREIGN KEY (DirectoryId) REFERENCES SharedDirectories(Id)
            );

            CREATE TABLE IF NOT EXISTS FileTags (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DirectoryId INTEGER NOT NULL,
                FilePath TEXT NOT NULL,
                UserId INTEGER NOT NULL,
                Tag TEXT NOT NULL DEFAULT '',
                Color TEXT NOT NULL DEFAULT '',
                Note TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (DirectoryId) REFERENCES SharedDirectories(Id),
                FOREIGN KEY (UserId) REFERENCES Users(Id)
            );

            CREATE TABLE IF NOT EXISTS DownloadStats (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DirectoryId INTEGER NOT NULL,
                FilePath TEXT NOT NULL,
                FileName TEXT NOT NULL,
                UserId INTEGER NOT NULL,
                DownloadCount INTEGER NOT NULL DEFAULT 0,
                LastDownloadedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (DirectoryId) REFERENCES SharedDirectories(Id),
                FOREIGN KEY (UserId) REFERENCES Users(Id)
            );

            CREATE TABLE IF NOT EXISTS Notifications (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                Type TEXT NOT NULL DEFAULT 'info',
                Title TEXT NOT NULL,
                Content TEXT NOT NULL DEFAULT '',
                IsRead INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (UserId) REFERENCES Users(Id)
            );
        ");

        await EnsureAdminUserAsync(connection);
        await EnsureDefaultConfigsAsync(connection);
        await EnsureLocalDisksAsync(connection);
        await MigrateAsync(connection);

        Log.Information("Database initialized successfully");
    }

    private async Task MigrateAsync(SqliteConnection connection)
    {
        var columns = await connection.QueryAsync<string>(
            "SELECT name FROM pragma_table_info('SharedDirectories')");
        var columnList = columns.ToList();

        if (!columnList.Contains("AllowSmb"))
        {
            await connection.ExecuteAsync(
                "ALTER TABLE SharedDirectories ADD COLUMN AllowSmb INTEGER NOT NULL DEFAULT 0");
        }

        if (!columnList.Contains("AllowWebDav"))
        {
            await connection.ExecuteAsync(
                "ALTER TABLE SharedDirectories ADD COLUMN AllowWebDav INTEGER NOT NULL DEFAULT 1");
        }
    }

    private async Task EnsureAdminUserAsync(SqliteConnection connection)
    {
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE Role = 'admin'");
        if (count == 0)
        {
            var salt = BCrypt.Net.BCrypt.GenerateSalt(12);
            var hash = BCrypt.Net.BCrypt.HashPassword("admin", salt);
            await connection.ExecuteAsync(@"
                INSERT INTO Users (Username, PasswordHash, Salt, Nickname, Role, IsEnabled)
                VALUES ('admin', @Hash, @Salt, 'Administrator', 'admin', 1)",
                new { Hash = hash, Salt = salt });
            Log.Information("Default admin user created (username: admin, password: admin)");
        }
    }

    private async Task EnsureDefaultConfigsAsync(SqliteConnection connection)
    {
        var configs = new[]
        {
            new { Key = "site_name", Value = "WinNAS", Category = "general", Desc = "站点名称" },
            new { Key = "allow_register", Value = "true", Category = "general", Desc = "允许自助注册" },
            new { Key = "allow_guest", Value = "true", Category = "general", Desc = "允许访客模式" },
            new { Key = "http_port", Value = "8080", Category = "network", Desc = "HTTP服务端口" },
            new { Key = "max_upload_size", Value = "1073741824", Category = "file", Desc = "最大上传大小(字节)" },
            new { Key = "duplicate_check", Value = "true", Category = "file", Desc = "重复文件检查" },
            new { Key = "auto_start", Value = "false", Category = "system", Desc = "开机自启动" },
            new { Key = "jwt_secret", Value = Guid.NewGuid().ToString("N"), Category = "security", Desc = "JWT密钥" },
            new { Key = "jwt_expire_hours", Value = "24", Category = "security", Desc = "JWT过期时间(小时)" },
            new { Key = "smb_enabled", Value = "false", Category = "network", Desc = "启用SMB共享" },
            new { Key = "webdav_enabled", Value = "true", Category = "network", Desc = "启用WebDAV服务" },
        };

        foreach (var cfg in configs)
        {
            await connection.ExecuteAsync(@"
                INSERT OR IGNORE INTO SystemConfigs (Key, Value, Category, Description)
                VALUES (@Key, @Value, @Category, @Desc)", cfg);
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root) && path.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return root;
        return path.TrimEnd('\\');
    }

    private async Task EnsureLocalDisksAsync(SqliteConnection connection)
    {
        var adminId = await connection.ExecuteScalarAsync<int?>("SELECT Id FROM Users WHERE Role = 'admin' LIMIT 1");
        if (adminId == null) return;

        var driveRoots = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .Select(d => d.RootDirectory.FullName.TrimEnd('\\'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allRecords = (await connection.QueryAsync<dynamic>("SELECT Id, Path FROM SharedDirectories")).ToList();
        foreach (var rec in allRecords)
        {
            var path = ((string)rec.Path).TrimEnd('\\');
            var root = Path.GetPathRoot(path)?.TrimEnd('\\') ?? "";
            if (!string.IsNullOrEmpty(root) && driveRoots.Contains(root) && !path.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.RootDirectory.FullName.TrimEnd('\\').Equals(root, StringComparison.OrdinalIgnoreCase));
                var label = drive != null && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? $"{drive.VolumeLabel} ({root})"
                    : $"本地磁盘 ({root})";
                var rootPath = root + "\\";
                await connection.ExecuteAsync("UPDATE SharedDirectories SET Path = @RootPath, Name = @Name, Description = '本地磁盘' WHERE Id = @Id",
                    new { RootPath = rootPath, Name = label, Id = (int)rec.Id });
                Log.Information("Fixed disk path: {OldPath} -> {NewPath}", path, rootPath);
            }
        }

        var existingPaths = (await connection.QueryAsync<string>(
            "SELECT Path FROM SharedDirectories")).Select(p => NormalizePath(p)).ToHashSet();

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            if (drive.DriveType != DriveType.Fixed) continue;

            var drivePath = drive.RootDirectory.FullName;
            if (existingPaths.Contains(NormalizePath(drivePath))) continue;

            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? $"本地磁盘 ({drive.Name.TrimEnd('\\')})"
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";

            await connection.ExecuteAsync(@"
                INSERT INTO SharedDirectories (Name, Path, Description, OwnerId, Visibility, AllowUpload, AllowDelete, AllowRename, AllowMove)
                VALUES (@Name, @Path, @Desc, @OwnerId, 'private', 1, 0, 0, 0)",
                new { Name = label, Path = drivePath, Desc = "本地磁盘", OwnerId = adminId.Value });

            Log.Information("Auto-added local disk: {Name} ({Path})", label, drivePath);
        }
    }
}
