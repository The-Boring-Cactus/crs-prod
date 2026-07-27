using System.Collections.Concurrent;
using Dapper;
using Newtonsoft.Json.Linq;

namespace Server.Core;

// Runs SQL-widget dashboard queries on a server-side schedule, independent of whether
// any browser has the dashboard open -- this is what backs a dashboard's per-widget
// "refresh every N minutes" setting. Previously, "refresh" only meant a client-side
// setInterval that stopped the moment the tab was closed; this makes refreshed data
// available (via GetWidgetCache) even to a public/shared viewer who opens the
// dashboard between scheduled ticks.
//
// The cache is in-memory only and is cleared on restart -- that's an acceptable
// tradeoff for a freshness cache: on restart it's simply empty until the next tick,
// and callers fall back to a live query when nothing is cached yet (same behavior
// as before this existed).
//
// Scoped to SqlWidget-type widgets whose stored SQL has no unresolved {{variable}}
// placeholders -- a headless background tick has no dashboard-viewer session to
// resolve per-viewer filter values against, so parameterized queries simply aren't
// refreshed this way (the client still runs those live, as it always has).
public static class ScheduledRefreshService
{
    public class CachedResult
    {
        public List<Dictionary<string, object>> Rows { get; set; }
        public List<object> Columns { get; set; }
        public DateTime RefreshedAt { get; set; }
        public string Error { get; set; }
    }

    private static readonly ConcurrentDictionary<string, CachedResult> _cache = new();
    private static Timer _timer;
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    public static void Start()
    {
        _timer ??= new Timer(_ => SafeTick(), null, TimeSpan.Zero, TickInterval);
    }

    public static CachedResult GetCached(string dashboardId, string widgetId)
    {
        _cache.TryGetValue($"{dashboardId}:{widgetId}", out var result);
        return result;
    }

    private static void SafeTick()
    {
        try { Tick(); }
        catch (Exception ex) { Console.WriteLine($"ScheduledRefreshService tick error: {ex.Message}"); }
    }

    private static void Tick()
    {
        var config = SetupConfig.Load();
        if (!config.IsConfigured) return;

        List<JObject> dashboards;
        try { dashboards = DatabasePersistence.LoadAllEntities("Dashboards"); }
        catch { return; } // DB not reachable / server still starting up

        foreach (var dash in dashboards)
        {
            // Postgres folds unquoted column names to all-lowercase (UserId -> "userid",
            // not "userId"), so a camelCase/PascalCase-only fallback chain would miss it.
            var dashboardId = dash["id"]?.ToString() ?? dash["Id"]?.ToString();
            var userId = dash["userid"]?.ToString() ?? dash["userId"]?.ToString() ?? dash["UserId"]?.ToString();
            var configRaw = dash["config"]?.ToString() ?? dash["Config"]?.ToString();
            if (string.IsNullOrEmpty(dashboardId) || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(configRaw))
                continue;

            JObject dashboardConfig;
            try { dashboardConfig = JObject.Parse(configRaw); } catch { continue; }

            // Dashboard.vue's in-memory ref is named `layout.componentes` (Spanish), but
            // saveToServer()/loadFromServer() actually persist/read it under the key
            // "components" (English) -- that's the real wire format to match here.
            var widgets = dashboardConfig["components"] as JArray;
            if (widgets == null) continue;

            List<JObject> connections = null; // lazy-loaded once per dashboard, only if a widget actually needs refreshing

            foreach (var widget in widgets)
            {
                if (widget["type"]?.ToString() != "SqlWidget") continue;

                var refreshMinutes = widget["refreshIntervalMinutes"]?.ToObject<int?>();
                if (refreshMinutes == null || refreshMinutes <= 0) continue;

                var widgetId = widget["i"]?.ToString();
                var databaseId = widget["databaseId"]?.ToString();
                var sqlCode = widget["sqlCode"]?.ToString();
                if (string.IsNullOrEmpty(widgetId) || string.IsNullOrEmpty(databaseId) || string.IsNullOrEmpty(sqlCode))
                    continue;
                if (sqlCode.Contains("{{")) continue; // has unresolved viewer-specific filter placeholders

                var cacheKey = $"{dashboardId}:{widgetId}";
                if (_cache.TryGetValue(cacheKey, out var existing) &&
                    DateTime.UtcNow - existing.RefreshedAt < TimeSpan.FromMinutes(refreshMinutes.Value))
                    continue; // not due yet

                connections ??= DatabasePersistence.LoadDatabaseConnections(userId);
                var connInfo = connections.FirstOrDefault(c => (c["id"]?.ToString() ?? c["Id"]?.ToString()) == databaseId);
                if (connInfo == null) continue;

                try
                {
                    var (rows, columns) = RunHeadlessQuery(connInfo, sqlCode);
                    _cache[cacheKey] = new CachedResult { Rows = rows, Columns = columns, RefreshedAt = DateTime.UtcNow };
                }
                catch (Exception ex)
                {
                    _cache[cacheKey] = new CachedResult { Rows = new(), Columns = new(), RefreshedAt = DateTime.UtcNow, Error = ex.Message };
                }
            }
        }
    }

    private static (List<Dictionary<string, object>> rows, List<object> columns) RunHeadlessQuery(JObject connInfo, string sql)
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
            _ => throw new InvalidOperationException($"Unsupported connection type for scheduled refresh: {type}")
        };

        using (conn)
        {
            conn.Open();
            var result = conn.Query(sql);

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
}
