using WinNASClient.Database;
using WinNASClient.Models;
using WinNASClient.Utils;
using Dapper;

namespace WinNASClient.Services;

public class ShareService
{
    private readonly DbRepository _repo;

    public ShareService(DbRepository repo)
    {
        _repo = repo;
    }

    public async Task<ShareLink> CreateShareLinkAsync(int directoryId, string path, int creatorId, string? password = null, DateTime? expiresAt = null, int maxDownloads = -1)
    {
        var link = new ShareLink
        {
            Code = CryptoHelper.GenerateShareCode(),
            DirectoryId = directoryId,
            Path = path,
            CreatorId = creatorId,
            Password = password ?? "",
            ExpiresAt = expiresAt,
            MaxDownloads = maxDownloads
        };

        var id = await _repo.ExecuteScalarAsync<int>(@"
            INSERT INTO ShareLinks (Code, Path, DirectoryId, CreatorId, Password, ExpiresAt, MaxDownloads)
            VALUES (@Code, @Path, @DirectoryId, @CreatorId, @Password, @ExpiresAt, @MaxDownloads);
            SELECT last_insert_rowid();", link);

        link.Id = id;
        return link;
    }

    public async Task<ShareLink?> GetShareLinkAsync(string code)
    {
        return await _repo.QueryFirstOrDefaultAsync<ShareLink>(
            "SELECT * FROM ShareLinks WHERE Code = @Code AND IsEnabled = 1", new { Code = code });
    }

    public async Task<IEnumerable<ShareLink>> GetShareLinksByUserAsync(int userId)
    {
        return await _repo.QueryAsync<ShareLink>(
            "SELECT * FROM ShareLinks WHERE CreatorId = @UserId ORDER BY CreatedAt DESC", new { UserId = userId });
    }

    public async Task<bool> ValidateShareAccessAsync(string code, string? password)
    {
        var link = await GetShareLinkAsync(code);
        if (link == null) return false;

        if (!string.IsNullOrEmpty(link.Password) && link.Password != password)
            return false;

        if (link.ExpiresAt.HasValue && link.ExpiresAt.Value < DateTime.UtcNow)
            return false;

        if (link.MaxDownloads > 0 && link.DownloadCount >= link.MaxDownloads)
            return false;

        return true;
    }

    public async Task IncrementDownloadCountAsync(int linkId)
    {
        await _repo.ExecuteAsync(@"
            UPDATE ShareLinks SET DownloadCount = DownloadCount + 1 WHERE Id = @Id", new { Id = linkId });
    }

    public async Task<bool> DisableShareLinkAsync(int linkId, int userId)
    {
        return await _repo.ExecuteAsync(@"
            UPDATE ShareLinks SET IsEnabled = 0 WHERE Id = @Id AND CreatorId = @UserId",
            new { Id = linkId, UserId = userId }) > 0;
    }

    public async Task<bool> EnableShareLinkAsync(int linkId, int userId)
    {
        return await _repo.ExecuteAsync(@"
            UPDATE ShareLinks SET IsEnabled = 1 WHERE Id = @Id AND CreatorId = @UserId",
            new { Id = linkId, UserId = userId }) > 0;
    }

    public async Task<bool> DeleteShareLinkAsync(int linkId, int userId)
    {
        return await _repo.ExecuteAsync(@"
            DELETE FROM ShareLinks WHERE Id = @Id AND CreatorId = @UserId",
            new { Id = linkId, UserId = userId }) > 0;
    }
}
