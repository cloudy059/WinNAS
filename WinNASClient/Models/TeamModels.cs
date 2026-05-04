namespace WinNASClient.Models;

public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public int CreatorId { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public long StorageLimit { get; set; } = -1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class TeamMember
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public int UserId { get; set; }
    public string Role { get; set; } = "member";
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public string? Username { get; set; }
    public string? Nickname { get; set; }
}

public class ChatMessage
{
    public int Id { get; set; }
    public int SenderId { get; set; }
    public int? ReceiverId { get; set; }
    public int? TeamId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string MessageType { get; set; } = "text";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? SenderName { get; set; }
    public string? ReceiverName { get; set; }
}

public class TeamFile
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string DirectoryPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Extension { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string Md5 { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public bool IsLocked { get; set; }
    public int? LockedBy { get; set; }
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class TeamFileVersion
{
    public int Id { get; set; }
    public int TeamFileId { get; set; }
    public int Version { get; set; }
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public int UploaderId { get; set; }
    public string ChangeNote { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
