using Microsoft.Data.Sqlite;
using Dapper;

namespace WinNASClient.Database;

public class DbRepository
{
    private readonly DbInitializer _dbInitializer;

    public DbRepository(DbInitializer dbInitializer)
    {
        _dbInitializer = dbInitializer;
    }

    private SqliteConnection CreateConnection() => _dbInitializer.CreateConnection();

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<T>(sql, param);
    }

    public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<T>(sql, param);
    }

    public async Task<int> ExecuteAsync(string sql, object? param = null)
    {
        using var conn = CreateConnection();
        return await conn.ExecuteAsync(sql, param);
    }

    public async Task<T> ExecuteScalarAsync<T>(string sql, object? param = null)
    {
        using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<T>(sql, param);
    }

    public async Task<IEnumerable<T>> QueryPagedAsync<T>(string tableName, int page, int pageSize, string where = "1=1", string orderBy = "Id DESC", object? param = null)
    {
        var offset = (page - 1) * pageSize;
        var sql = $"SELECT * FROM {tableName} WHERE {where} ORDER BY {orderBy} LIMIT @PageSize OFFSET @Offset";
        using var conn = CreateConnection();
        var dp = new DynamicParameters(param);
        dp.Add("PageSize", pageSize);
        dp.Add("Offset", offset);
        return await conn.QueryAsync<T>(sql, dp);
    }

    public async Task<int> CountAsync(string tableName, string where = "1=1")
    {
        var sql = $"SELECT COUNT(*) FROM {tableName} WHERE {where}";
        using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<int>(sql);
    }
}
