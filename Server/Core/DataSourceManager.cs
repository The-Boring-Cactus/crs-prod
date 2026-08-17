using Dapper;
using System.Data;
namespace Server.Core;

public class DataSourceManager
{
    private readonly ReportsCache _cache;
    private readonly Dictionary<string, IDbConnection> _connections;
    
    public DataSourceManager(ReportsCache cache)
    {
        _cache = cache;
        _connections = new Dictionary<string, IDbConnection>();
    }
    
    public async Task<IEnumerable<dynamic>> ExecuteQueryAsync(
        string connectionKey,
        string sql,
        object parameters = null,
        TimeSpan? cacheAge = null,
        bool forceRefresh = false)
    {
        var queryKey = GenerateQueryKey(connectionKey, sql, parameters);
        var maxAge = cacheAge ?? TimeSpan.FromMinutes(15);

        // A caller that explicitly asked to bypass the cache (e.g. SQL Editor's "Force
        // Refresh" button) gets a guaranteed live run by evicting the entry first, rather
        // than needing a separate uncached code path.
        if (forceRefresh) _cache.InvalidateQuery(queryKey);

        return await _cache.GetOrExecuteAsync(queryKey, async () =>
        {
            var connection = GetConnection(connectionKey);
            return await connection.QueryAsync(sql, parameters);
        }, maxAge);
    }
    
    public void InvalidateQuery(string queryKey)
    {
        _cache.InvalidateQuery(queryKey);
    }
    
    private string GenerateQueryKey(string connectionKey, string sql, object parameters)
    {
        var paramHash = parameters?.GetHashCode().ToString() ?? "no-params";
        return $"{connectionKey}:{sql.GetHashCode()}:{paramHash}";
    }
    
    private IDbConnection GetConnection(string key)
    {
        if (!_connections.ContainsKey(key))
            throw new ArgumentException($"Connection '{key}' not configured");
            
        var connection = _connections[key];
        if (connection.State != ConnectionState.Open)
            connection.Open();
            
        return connection;
    }
    
    public void AddConnection(string key, IDbConnection connection)
    {
        _connections[key] = connection;
    }
}