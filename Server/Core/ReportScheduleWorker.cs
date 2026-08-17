using System.Text;
using Newtonsoft.Json.Linq;

namespace Server.Core;

// Runs saved SQL Editor queries on a server-side schedule and emails the results to
// configured recipients as an HTML table + CSV attachment, independent of whether any
// browser has the query open. A schedule lives on the SqlScripts row itself (the
// "Schedule" column, a JSON blob) rather than a separate table, so it travels with the
// script it belongs to and there's exactly one to manage per saved query.
//
// Unlike ScheduledRefreshService's in-memory "due" tracking (fine for a freshness
// cache that can just refresh again after a restart), a delivery must not be silently
// skipped or re-sent -- so "last run" here is the persisted Schedule.lastRunAt, checked
// against the current time on each minute-granularity tick.
public static class ReportScheduleWorker
{
    private static Timer _timer;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    public static void Start()
    {
        _timer ??= new Timer(_ => SafeTick(), null, TimeSpan.Zero, TickInterval);
    }

    private static void SafeTick()
    {
        try { Tick(); }
        catch (Exception ex) { Console.WriteLine($"ReportScheduleWorker tick error: {ex.Message}"); }
    }

    private static void Tick()
    {
        var config = SetupConfig.Load();
        if (!config.IsConfigured) return;

        List<JObject> scripts;
        try { scripts = DatabasePersistence.LoadAllScheduledSqlScripts(); }
        catch { return; } // DB not reachable / server still starting up

        foreach (var script in scripts)
        {
            try
            {
                var scheduleRaw = script["schedule"]?.ToString() ?? script["Schedule"]?.ToString();
                if (string.IsNullOrWhiteSpace(scheduleRaw)) continue;

                JObject schedule;
                try { schedule = JObject.Parse(scheduleRaw); } catch { continue; }

                if (schedule["enabled"]?.ToObject<bool?>() != true) continue;
                if (!IsDue(schedule)) continue;

                RunAndDeliver(script, schedule, persist: true);
            }
            catch (Exception ex)
            {
                var id = script["id"]?.ToString() ?? script["Id"]?.ToString();
                Console.WriteLine($"ReportScheduleWorker: script {id} failed: {ex.Message}");
            }
        }
    }

    private static bool IsDue(JObject schedule)
    {
        var now = DateTime.UtcNow;
        var lastRunAt = schedule["lastRunAt"]?.ToObject<DateTime?>();
        var frequency = schedule["frequency"]?.ToString() ?? "daily";

        if (frequency == "hourly")
        {
            var everyN = Math.Max(1, schedule["intervalHours"]?.ToObject<int?>() ?? 1);
            if (lastRunAt == null) return true;
            return now - lastRunAt.Value >= TimeSpan.FromHours(everyN) - TimeSpan.FromSeconds(30);
        }

        var hour = Math.Clamp(schedule["hour"]?.ToObject<int?>() ?? 8, 0, 23);
        var minute = Math.Clamp(schedule["minute"]?.ToObject<int?>() ?? 0, 0, 59);

        if (frequency == "weekly")
        {
            var days = schedule["daysOfWeek"]?.ToObject<List<int>>() ?? new List<int> { 1 };
            if (!days.Contains((int)now.DayOfWeek)) return false;
        }

        // "daily" (and the day-matched branch of "weekly"): due once the scheduled
        // time-of-day has passed today and hasn't already run since then. A tick that
        // finds the server was down past the scheduled time still fires once when it
        // comes back, rather than silently skipping that day's delivery.
        var scheduledToday = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0, DateTimeKind.Utc);
        if (now < scheduledToday) return false;
        if (lastRunAt != null && lastRunAt.Value >= scheduledToday) return false;
        return true;
    }

    // Executes the script's query and emails the result to `schedule`'s recipients.
    // `schedule` may be a draft (e.g. from the "send test now" button, not yet saved)
    // -- `persist` controls whether the run outcome is written back to the script's
    // Schedule column, so a test send never disturbs a saved schedule's lastRunAt/status.
    public static (bool success, string message) RunAndDeliver(JObject script, JObject schedule, bool persist)
    {
        var userId = script["userid"]?.ToString() ?? script["userId"]?.ToString() ?? script["UserId"]?.ToString();
        var scriptId = script["id"]?.ToString() ?? script["Id"]?.ToString();
        var scriptName = script["name"]?.ToString() ?? script["Name"]?.ToString() ?? "Report";
        var sql = script["code"]?.ToString() ?? script["Code"]?.ToString();
        var databaseId = script["databaseconnectionid"]?.ToString() ?? script["databaseConnectionId"]?.ToString() ?? script["DatabaseConnectionId"]?.ToString();

        var recipients = (schedule["recipients"]?.ToObject<List<string>>() ?? new List<string>())
            .Select(r => r?.Trim()).Where(r => !string.IsNullOrEmpty(r)).Distinct().ToList();

        if (recipients.Count == 0)
            return (false, "No recipients configured.");

        string error = null;
        List<Dictionary<string, object>> rows = null;
        List<object> columns = null;

        try
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(databaseId) || string.IsNullOrEmpty(sql))
                throw new InvalidOperationException("This report is missing a database connection or query.");

            var connections = DatabasePersistence.LoadDatabaseConnections(userId);
            var connInfo = connections.FirstOrDefault(c => (c["id"]?.ToString() ?? c["Id"]?.ToString()) == databaseId);
            if (connInfo == null)
                throw new InvalidOperationException("The database connection for this report no longer exists.");

            (rows, columns) = HeadlessQueryExecutor.RunQuery(connInfo, sql);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        var subject = error == null
            ? $"{scriptName} — scheduled report"
            : $"{scriptName} — scheduled report failed";
        var html = error == null
            ? BuildHtmlEmail(scriptName, columns, rows)
            : $"<p>The scheduled report <strong>{System.Net.WebUtility.HtmlEncode(scriptName)}</strong> failed to run:</p><p>{System.Net.WebUtility.HtmlEncode(error)}</p>";

        IEnumerable<(string, byte[], string)> attachments = null;
        if (error == null)
        {
            var csvBytes = Encoding.UTF8.GetBytes(BuildCsv(columns, rows));
            attachments = new[] { ($"{SanitizeFileName(scriptName)}.csv", csvBytes, "text/csv") };
        }

        var failures = new List<string>();
        foreach (var to in recipients)
        {
            var (sent, sendMessage) = EmailService.SendEmailAsync(to, subject, html, attachments).GetAwaiter().GetResult();
            if (!sent) failures.Add($"{to}: {sendMessage}");
        }

        if (persist && !string.IsNullOrEmpty(scriptId) && !string.IsNullOrEmpty(userId))
        {
            schedule["lastRunAt"] = DateTime.UtcNow;
            schedule["lastStatus"] = error == null ? "success" : "error";
            schedule["lastError"] = error;
            DatabasePersistence.UpdateScriptSchedule(userId, scriptId, schedule.ToString(Newtonsoft.Json.Formatting.None));
        }

        // The query error is the actionable problem even when the failure-notice email
        // itself sent fine, so it takes priority in what's reported back to the caller.
        if (error != null) return (false, error);
        if (failures.Count > 0) return (false, string.Join("; ", failures));
        return (true, $"Sent to {recipients.Count} recipient(s).");
    }

    // Columns come from HeadlessQueryExecutor.RunQuery as anonymous { field, header }
    // objects; dynamic dispatch reads the field name without a shared type to reference.
    private static List<string> ExtractFields(List<object> columns)
    {
        var fields = new List<string>();
        foreach (var c in columns)
        {
            try { dynamic d = c; fields.Add((string)d.field); }
            catch { fields.Add(c?.ToString() ?? ""); }
        }
        return fields;
    }

    private static string BuildHtmlEmail(string title, List<object> columns, List<Dictionary<string, object>> rows)
    {
        var fields = ExtractFields(columns);

        var sb = new StringBuilder();
        sb.Append($"<p>Scheduled report: <strong>{System.Net.WebUtility.HtmlEncode(title)}</strong></p>");
        sb.Append("<table style=\"border-collapse:collapse;font-family:sans-serif;font-size:13px\">");
        sb.Append("<thead><tr>");
        foreach (var f in fields)
            sb.Append($"<th style=\"border:1px solid #e4e4e7;padding:6px 10px;background:#f4f4f5;text-align:left\">{System.Net.WebUtility.HtmlEncode(f)}</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var row in rows.Take(200))
        {
            sb.Append("<tr>");
            foreach (var f in fields)
                sb.Append($"<td style=\"border:1px solid #e4e4e7;padding:6px 10px\">{System.Net.WebUtility.HtmlEncode(row.TryGetValue(f, out var v) ? v?.ToString() ?? "" : "")}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
        if (rows.Count > 200)
            sb.Append($"<p style=\"color:#71717a;font-size:12px\">Showing the first 200 of {rows.Count} rows — see the attached CSV for the full result.</p>");
        return sb.ToString();
    }

    private static string BuildCsv(List<object> columns, List<Dictionary<string, object>> rows)
    {
        var fields = ExtractFields(columns);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", fields.Select(CsvEscape)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", fields.Select(f => CsvEscape(row.TryGetValue(f, out var v) ? v?.ToString() ?? "" : ""))));
        return sb.ToString();
    }

    private static string CsvEscape(string value)
    {
        value ??= "";
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(clean) ? "report" : clean;
    }
}
