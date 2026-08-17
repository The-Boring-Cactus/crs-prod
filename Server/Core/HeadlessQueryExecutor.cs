using Dapper;
using Newtonsoft.Json.Linq;

namespace Server.Core;

// A cache-friendly, JSON-serializable stand-in for the (rows, columns) tuple RunQuery
// returns -- ReportsCache round-trips cached values through System.Text.Json, and named
// properties serialize more predictably there than a ValueTuple.
public class HeadlessQueryResult
{
    public List<Dictionary<string, object>> Rows { get; set; } = new();
    public List<object> Columns { get; set; } = new();
}

// Runs a SQL query against a saved connection outside of any live WebSocket session --
// shared by every background feature that has to execute a user's query without a
// browser attached (dashboard widget auto-refresh, scheduled report delivery, public
// share-link views).
public static class HeadlessQueryExecutor
{
    public static (List<Dictionary<string, object>> rows, List<object> columns) RunQuery(JObject connInfo, string sql, object parameters = null)
    {
        // Field lookups mirror WebSocketManager.RegisterUserDatabaseConnections: Postgres
        // folds unquoted column names to all-lowercase (DatabaseName -> "databasename"),
        // so the all-lowercase variant must be checked, not just camelCase/PascalCase.
        var type = (connInfo["type"]?.ToString() ?? connInfo["Type"]?.ToString() ?? "").ToLower();
        var host = connInfo["host"]?.ToString() ?? connInfo["Host"]?.ToString();
        var db = connInfo["databasename"]?.ToString() ?? connInfo["database"]?.ToString() ?? connInfo["DatabaseName"]?.ToString();
        var user = connInfo["username"]?.ToString() ?? connInfo["Username"]?.ToString();
        var pass = connInfo["password"]?.ToString() ?? connInfo["Password"]?.ToString();
        int.TryParse(connInfo["port"]?.ToString() ?? connInfo["Port"]?.ToString(), out int port);
        var connectionString = connInfo["connectionstring"]?.ToString() ?? connInfo["connectionString"]?.ToString() ?? connInfo["ConnectionString"]?.ToString();
        if (string.IsNullOrWhiteSpace(connectionString)) connectionString = null;

        System.Data.IDbConnection conn = type switch
        {
            "mssql" => new Microsoft.Data.SqlClient.SqlConnection(
                connectionString ?? $"Server={host},{port};Database={db};User Id={user};Password={pass};TrustServerCertificate=True;"),
            "postgresql" => new Npgsql.NpgsqlConnection(
                connectionString ?? $"Host={host};Port={port};Database={db};Username={user};Password={pass};"),
            "mysql" => new MySqlConnector.MySqlConnection(
                connectionString ?? $"Server={host};Port={port};Database={db};Uid={user};Pwd={pass};"),
            _ => throw new InvalidOperationException($"Unsupported connection type for headless query execution: {type}")
        };

        using (conn)
        {
            conn.Open();
            var result = conn.Query(sql, parameters);

            var rows = new List<Dictionary<string, object>>();
            var columns = new List<object>();
            bool colsExtracted = false;
            foreach (var row in result)
            {
                var rowDict = ((IDictionary<string, object>)row).ToDictionary(kv => kv.Key, kv => kv.Value);
                if (!colsExtracted)
                {
                    foreach (var key in rowDict.Keys) columns.Add(new { field = key, header = key });
                    colsExtracted = true;
                }
                rows.Add(rowDict);
            }
            return (rows, columns);
        }
    }

    // Cached flavor of RunQuery, backed by the same ReportsCache the authenticated in-app
    // path (DataSourceManager.ExecuteQueryAsync) uses -- one shared result cache instead of
    // each background/public feature keeping its own. Falls back to a live (uncached) run if
    // the cache hasn't been constructed yet (Program.cs always constructs it before any
    // caller of this method can run, so that's a defensive fallback, not an expected path).
    //
    // Cache key note: `parameters` is hashed with the same convention DataSourceManager uses
    // (plain object.GetHashCode()) for consistency -- that means parameter *values* only
    // produce a stable key when parameters is an anonymous object (whose GetHashCode is
    // value-based); a Dictionary<string, object> parameters bag hashes by reference and so
    // never hits cache. Known, pre-existing limitation of this key scheme, not something this
    // method tries to fix.
    public static async Task<HeadlessQueryResult> RunQueryCachedAsync(JObject connInfo, string sql, TimeSpan maxAge, object parameters = null)
    {
        var cache = ReportsCache.Instance;
        if (cache == null)
        {
            var (liveRows, liveColumns) = RunQuery(connInfo, sql, parameters);
            return new HeadlessQueryResult { Rows = liveRows, Columns = liveColumns };
        }

        var connId = connInfo["id"]?.ToString() ?? connInfo["Id"]?.ToString() ?? "unknown";
        var paramHash = parameters?.GetHashCode().ToString() ?? "no-params";
        var key = $"{connId}:{sql.GetHashCode()}:{paramHash}";

        return await cache.GetOrExecuteAsync(key, () =>
        {
            var (rows, columns) = RunQuery(connInfo, sql, parameters);
            return Task.FromResult(new HeadlessQueryResult { Rows = rows, Columns = columns });
        }, maxAge);
    }
}
