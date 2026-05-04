using WinNASClient.Database;
using WinNASClient.Models;
using WinNASClient.Utils;
using Dapper;

namespace WinNASClient.Services;

public class AuthService
{
    private readonly DbRepository _repo;
    private readonly string _jwtSecret;
    private readonly int _jwtExpireHours;

    public AuthService(DbRepository repo, string jwtSecret, int jwtExpireHours)
    {
        _repo = repo;
        _jwtSecret = jwtSecret;
        _jwtExpireHours = jwtExpireHours;
    }

    public async Task<(bool Success, string Message, string? Token, User? User)> LoginAsync(string username, string password, string ipAddress, string deviceInfo)
    {
        var user = await _repo.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE Username = @Username", new { Username = username });

        if (user == null)
            return (false, "用户名或密码错误", null, null);

        if (!user.IsEnabled)
            return (false, "账号已被禁用", null, null);

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return (false, "用户名或密码错误", null, null);

        var token = JwtHelper.GenerateToken(user.Id, user.Username, user.Role, _jwtSecret, _jwtExpireHours);

        await _repo.ExecuteAsync(@"
            INSERT INTO UserSessions (UserId, Token, DeviceInfo, IpAddress, ExpiresAt)
            VALUES (@UserId, @Token, @DeviceInfo, @IpAddress, @ExpiresAt)",
            new
            {
                UserId = user.Id,
                Token = token,
                DeviceInfo = deviceInfo,
                IpAddress = ipAddress,
                ExpiresAt = DateTime.UtcNow.AddHours(_jwtExpireHours)
            });

        return (true, "登录成功", token, user);
    }

    public async Task<(bool Success, string Message, User? User)> RegisterAsync(string username, string password, string nickname, bool allowRegister)
    {
        if (!allowRegister)
            return (false, "管理员未开启自助注册", null);

        if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
            return (false, "用户名至少3个字符", null);

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            return (false, "密码至少6个字符", null);

        var exists = await _repo.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Users WHERE Username = @Username", new { Username = username });
        if (exists > 0)
            return (false, "用户名已存在", null);

        var salt = BCrypt.Net.BCrypt.GenerateSalt(12);
        var hash = BCrypt.Net.BCrypt.HashPassword(password, salt);

        var nick = string.IsNullOrWhiteSpace(nickname) ? username : nickname;

        await _repo.ExecuteAsync(@"
            INSERT INTO Users (Username, PasswordHash, Salt, Nickname, Role, IsEnabled)
            VALUES (@Username, @Hash, @Salt, @Nickname, 'user', 1)",
            new { Username = username, Hash = hash, Salt = salt, Nickname = nick });

        var user = await _repo.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE Username = @Username", new { Username = username });

        return (true, "注册成功", user);
    }

    public async Task<User?> GetUserByIdAsync(int userId)
    {
        return await _repo.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE Id = @Id", new { Id = userId });
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        return await _repo.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE Username = @Username", new { Username = username });
    }

    public async Task<IEnumerable<User>> GetAllUsersAsync()
    {
        return await _repo.QueryAsync<User>("SELECT Id, Username, Nickname, Avatar, Role, IsEnabled, StorageLimit, CreatedAt FROM Users");
    }

    public async Task<bool> UpdateUserAsync(User user)
    {
        return await _repo.ExecuteAsync(@"
            UPDATE Users SET Nickname = @Nickname, Avatar = @Avatar, Role = @Role,
            IsEnabled = @IsEnabled, StorageLimit = @StorageLimit, UpdatedAt = datetime('now')
            WHERE Id = @Id", user) > 0;
    }

    public async Task<bool> ChangePasswordAsync(int userId, string oldPassword, string newPassword)
    {
        var user = await GetUserByIdAsync(userId);
        if (user == null) return false;

        if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
            return false;

        var salt = BCrypt.Net.BCrypt.GenerateSalt(12);
        var hash = BCrypt.Net.BCrypt.HashPassword(newPassword, salt);

        return await _repo.ExecuteAsync(@"
            UPDATE Users SET PasswordHash = @Hash, Salt = @Salt, UpdatedAt = datetime('now')
            WHERE Id = @Id", new { Hash = hash, Salt = salt, Id = userId }) > 0;
    }

    public async Task<bool> DeleteUserAsync(int userId)
    {
        return await _repo.ExecuteAsync("DELETE FROM Users WHERE Id = @Id", new { Id = userId }) > 0;
    }

    public async Task LogoutAsync(string token)
    {
        await _repo.ExecuteAsync("DELETE FROM UserSessions WHERE Token = @Token", new { Token = token });
    }
}
