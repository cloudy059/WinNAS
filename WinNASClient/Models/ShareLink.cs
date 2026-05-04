namespace WinNASClient.Models;

public class ShareLink
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int DirectoryId { get; set; }
    public int CreatorId { get; set; }
    public string Password { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
    public int MaxDownloads { get; set; } = -1;
    public int DownloadCount { get; set; } = 0;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
