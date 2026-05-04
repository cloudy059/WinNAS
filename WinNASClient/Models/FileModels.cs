namespace WinNASClient.Models;

public class SharedDirectory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public string Visibility { get; set; } = "private";
    public bool AllowUpload { get; set; } = true;
    public bool AllowDelete { get; set; } = false;
    public bool AllowRename { get; set; } = false;
    public bool AllowMove { get; set; } = false;
    public bool AllowSmb { get; set; } = false;
    public bool AllowWebDav { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class DirectoryPermission
{
    public int Id { get; set; }
    public int DirectoryId { get; set; }
    public int UserId { get; set; }
    public bool CanRead { get; set; } = true;
    public bool CanWrite { get; set; } = false;
    public bool CanDelete { get; set; } = false;
    public bool CanRename { get; set; } = false;
    public bool CanMove { get; set; } = false;
}

public class FileRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string DirectoryPath { get; set; } = string.Empty;
    public int DirectoryId { get; set; }
    public long Size { get; set; }
    public string Extension { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string Md5 { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
