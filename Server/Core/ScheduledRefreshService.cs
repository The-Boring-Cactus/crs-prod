using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;

namespace Server.Core;

// Runs SQL-widget dashboard queries on a server-side schedule, independent of whether
// any browser has the dashboard open -- this is what backs a dashboard's per-widget
// "refresh every N minutes" setting. Previously, "refresh" only meant a client-side
// setInterval that stopped the moment the tab was closed; this makes refreshed data
// available (via GetWidgetCache) even to a public/shared viewer who opens the
// dashboard between scheduled ticks.
//
// This "outer" cache (_cache below, keyed by dashboardId:widgetId) is what enforces each
// widget's own refreshIntervalMinutes and what GetWidgetCache reads -- that contract is
// unchanged. What DOES query the database on a tick now goes through HeadlessQueryExecutor's
// RunQueryCachedAsync, backed by the same ReportsCache the authenticated ExecuteSql path
// uses, so an identical query already run elsewhere is reused instead of re-hitting the DB.
//
// The outer cache is in-memory only and is cleared on restart -- that's an acceptable
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
    private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(1);

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
                    // Routed through the same shared ReportsCache the authenticated ExecuteSql
                    // path uses (a short dedup window, not this widget's own refresh interval --
                    // that's already been enforced by the TryGetValue staleness check above).
                    // If the exact same query was just run elsewhere (SQL Editor, another
                    // widget), this tick reuses that result instead of hitting the DB again.
                    var result = HeadlessQueryExecutor.RunQueryCachedAsync(connInfo, sqlCode, DedupWindow).GetAwaiter().GetResult();
                    _cache[cacheKey] = new CachedResult { Rows = result.Rows, Columns = result.Columns, RefreshedAt = DateTime.UtcNow };
                }
                catch (Exception ex)
                {
                    _cache[cacheKey] = new CachedResult { Rows = new(), Columns = new(), RefreshedAt = DateTime.UtcNow, Error = ex.Message };
                }
            }
        }
    }
}
