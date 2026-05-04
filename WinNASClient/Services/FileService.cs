using WinNASClient.Database;
using WinNASClient.Models;
using WinNASClient.Utils;
using Dapper;

namespace WinNASClient.Services;

public class FileService
{
    private readonly DbRepository _repo;
    private readonly bool _duplicateCheck;

    public FileService(DbRepository repo, bool duplicateCheck)
    {
        _repo = repo;
        _duplicateCheck = duplicateCheck;
    }

    public async Task<List<SharedDirectory>> GetAllDirectoriesAsync()
    {
        return (await _repo.QueryAsync<SharedDirectory>("SELECT * FROM SharedDirectories")).ToList();
    }

    public async Task<IEnumerable<SharedDirectory>> GetSharedDirectoriesAsync(int userId)
    {
        var owned = await _repo.QueryAsync<SharedDirectory>(
            "SELECT * FROM SharedDirectories WHERE OwnerId = @UserId", new { UserId = userId });

        var permitted = await _repo.QueryAsync<SharedDirectory>(@"
            SELECT sd.* FROM SharedDirectories sd
            INNER JOIN DirectoryPermissions dp ON sd.Id = dp.DirectoryId
            WHERE dp.UserId = @UserId AND dp.CanRead = 1", new { UserId = userId });

        var publicDirs = await _repo.QueryAsync<SharedDirectory>(
            "SELECT * FROM SharedDirectories WHERE Visibility = 'public'");

        return owned.Union(permitted).Union(publicDirs).DistinctBy(d => d.Id);
    }

    public async Task<SharedDirectory?> GetDirectoryAsync(int dirId)
    {
        return await _repo.QueryFirstOrDefaultAsync<SharedDirectory>(
            "SELECT * FROM SharedDirectories WHERE Id = @Id", new { Id = dirId });
    }

    public async Task<SharedDirectory> CreateDirectoryAsync(SharedDirectory dir)
    {
        if (!Directory.Exists(dir.Path))
            Directory.CreateDirectory(dir.Path);

        var id = await _repo.ExecuteScalarAsync<int>(@"
            INSERT INTO SharedDirectories (Name, Path, Description, OwnerId, Visibility, AllowUpload, AllowDelete, AllowRename, AllowMove)
            VALUES (@Name, @Path, @Description, @OwnerId, @Visibility, @AllowUpload, @AllowDelete, @AllowRename, @AllowMove);
            SELECT last_insert_rowid();", dir);

        dir.Id = id;
        return dir;
    }

    public async Task<bool> UpdateDirectoryAsync(SharedDirectory dir)
    {
        return await _repo.ExecuteAsync(@"
            UPDATE SharedDirectories SET Name = @Name, Description = @Description,
            Visibility = @Visibility, AllowUpload = @AllowUpload, AllowDelete = @AllowDelete,
            AllowRename = @AllowRename, AllowMove = @AllowMove,
            AllowSmb = @AllowSmb, AllowWebDav = @AllowWebDav, UpdatedAt = datetime('now')
            WHERE Id = @Id", dir) > 0;
    }

    public async Task<bool> DeleteDirectoryAsync(int dirId)
    {
        await _repo.ExecuteAsync("DELETE FROM DirectoryPermissions WHERE DirectoryId = @Id", new { Id = dirId });
        await _repo.ExecuteAsync("DELETE FROM FileRecords WHERE DirectoryId = @Id", new { Id = dirId });
        return await _repo.ExecuteAsync("DELETE FROM SharedDirectories WHERE Id = @Id", new { Id = dirId }) > 0;
    }

    public async Task<string?> GetConfigAsync(string key)
    {
        return await _repo.QueryFirstOrDefaultAsync<string>(
            "SELECT Value FROM SystemConfigs WHERE Key = @Key", new { Key = key });
    }

    public async Task SetConfigAsync(string key, string value)
    {
        await _repo.ExecuteAsync(
            "INSERT OR REPLACE INTO SystemConfigs (Key, Value, Category, Description, UpdatedAt) " +
            "VALUES (@Key, @Value, " +
            "COALESCE((SELECT Category FROM SystemConfigs WHERE Key = @Key), 'system'), " +
            "COALESCE((SELECT Description FROM SystemConfigs WHERE Key = @Key), ''), " +
            "datetime('now'))",
            new { Key = key, Value = value });
    }

    public async Task<bool> HasPermissionAsync(int dirId, int userId, string permission)
    {
        var dir = await GetDirectoryAsync(dirId);
        if (dir == null) return false;

        if (dir.OwnerId == userId) return true;
        if (dir.Visibility == "public" && permission == "CanRead") return true;

        if (permission == "CanRead") return true;
        if (permission == "CanWrite" && dir.AllowUpload) return true;
        if (permission == "CanDelete" && dir.AllowDelete) return true;
        if (permission == "CanRename" && dir.AllowRename) return true;
        if (permission == "CanMove" && dir.AllowMove) return true;

        var perm = await _repo.QueryFirstOrDefaultAsync<DirectoryPermission>(
            "SELECT * FROM DirectoryPermissions WHERE DirectoryId = @DirId AND UserId = @UserId",
            new { DirId = dirId, UserId = userId });

        if (perm == null) return false;

        return permission switch
        {
            "CanRead" => perm.CanRead,
            "CanWrite" => perm.CanWrite,
            "CanDelete" => perm.CanDelete,
            "CanRename" => perm.CanRename,
            "CanMove" => perm.CanMove,
            _ => false
        };
    }

    public async Task SetPermissionAsync(DirectoryPermission perm)
    {
        await _repo.ExecuteAsync(@"
            INSERT OR REPLACE INTO DirectoryPermissions (DirectoryId, UserId, CanRead, CanWrite, CanDelete, CanRename, CanMove)
            VALUES (@DirectoryId, @UserId, @CanRead, @CanWrite, @CanDelete, @CanRename, @CanMove)", perm);
    }

    public List<FileItemInfo> ListFiles(string directoryPath, string? subPath = null)
    {
        var fullPath = string.IsNullOrEmpty(subPath) ? directoryPath : Path.Combine(directoryPath, subPath);
        var result = new List<FileItemInfo>();

        if (!Directory.Exists(fullPath)) return result;

        var dirInfo = new DirectoryInfo(fullPath);

        foreach (var dir in dirInfo.GetDirectories())
        {
            result.Add(new FileItemInfo
            {
                Name = dir.Name,
                Path = dir.FullName,
                RelativePath = dir.FullName.Substring(directoryPath.Length).TrimStart('\\', '/'),
                IsDirectory = true,
                Size = 0,
                Extension = "",
                LastModified = dir.LastWriteTimeUtc
            });
        }

        foreach (var file in dirInfo.GetFiles())
        {
            result.Add(new FileItemInfo
            {
                Name = file.Name,
                Path = file.FullName,
                RelativePath = file.FullName.Substring(directoryPath.Length).TrimStart('\\', '/'),
                IsDirectory = false,
                Size = file.Length,
                Extension = file.Extension.ToLowerInvariant(),
                LastModified = file.LastWriteTimeUtc
            });
        }

        return result;
    }

    public async Task<(bool IsDuplicate, string? ExistingPath)> CheckDuplicateAsync(string directoryPath, string fileName)
    {
        if (!_duplicateCheck) return (false, null);

        var fullPath = Path.Combine(directoryPath, fileName);
        if (File.Exists(fullPath))
            return (true, fullPath);

        return (false, null);
    }

    public async Task<FileRecord> RecordFileAsync(FileRecord record)
    {
        var id = await _repo.ExecuteScalarAsync<int>(@"
            INSERT INTO FileRecords (Name, Path, DirectoryPath, DirectoryId, Size, Extension, MimeType, Md5, OwnerId)
            VALUES (@Name, @Path, @DirectoryPath, @DirectoryId, @Size, @Extension, @MimeType, @Md5, @OwnerId);
            SELECT last_insert_rowid();", record);

        record.Id = id;
        return record;
    }

    public async Task<bool> RenameFileAsync(int recordId, string newName)
    {
        var record = await _repo.QueryFirstOrDefaultAsync<FileRecord>(
            "SELECT * FROM FileRecords WHERE Id = @Id", new { Id = recordId });
        if (record == null) return false;

        var newPath = Path.Combine(Path.GetDirectoryName(record.Path)!, newName);

        if (File.Exists(record.Path))
            File.Move(record.Path, newPath);
        else if (Directory.Exists(record.Path))
            Directory.Move(record.Path, newPath);

        return await _repo.ExecuteAsync(@"
            UPDATE FileRecords SET Name = @Name, Path = @NewPath, UpdatedAt = datetime('now')
            WHERE Id = @Id", new { Name = newName, NewPath = newPath, Id = recordId }) > 0;
    }

    public async Task<bool> DeleteFileAsync(int recordId)
    {
        var record = await _repo.QueryFirstOrDefaultAsync<FileRecord>(
            "SELECT * FROM FileRecords WHERE Id = @Id", new { Id = recordId });
        if (record == null) return false;

        if (File.Exists(record.Path))
            File.Delete(record.Path);
        else if (Directory.Exists(record.Path))
            Directory.Delete(record.Path, true);

        return await _repo.ExecuteAsync("DELETE FROM FileRecords WHERE Id = @Id", new { Id = recordId }) > 0;
    }

    public async Task<bool> MoveFileAsync(int recordId, string targetDirectoryPath)
    {
        var record = await _repo.QueryFirstOrDefaultAsync<FileRecord>(
            "SELECT * FROM FileRecords WHERE Id = @Id", new { Id = recordId });
        if (record == null) return false;

        var newPath = Path.Combine(targetDirectoryPath, record.Name);

        if (File.Exists(record.Path))
            File.Move(record.Path, newPath);
        else if (Directory.Exists(record.Path))
            Directory.Move(record.Path, newPath);

        return await _repo.ExecuteAsync(@"
            UPDATE FileRecords SET Path = @NewPath, DirectoryPath = @DirPath, UpdatedAt = datetime('now')
            WHERE Id = @Id", new { NewPath = newPath, DirPath = targetDirectoryPath, Id = recordId }) > 0;
    }
}

public class FileItemInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public string Extension { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}
