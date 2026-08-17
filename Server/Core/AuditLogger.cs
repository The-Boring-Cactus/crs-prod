using Dapper;

namespace Server.Core;

// Writes to the AuditLog table. Username/ResourceName are captured at write time (not
// joined live) so the trail stays readable even after the actor's account or the resource
// itself is later renamed or deleted. Every call is wrapped in try/catch -- the audit trail
// is best-effort observability, and a logging failure must never break the operation it's
// recording.
public static class AuditLogger
{
    public static void Log(string userId, string username, string action, string resourceType = null, string resourceId = null, string resourceName = null, string details = null)
    {
        try
        {
            using var conn = DatabasePersistence.CreateConnection();
            if (conn == null) return;
            conn.Open();

            conn.Execute(@"INSERT INTO AuditLog (Id, UserId, Username, Action, ResourceType, ResourceId, ResourceName, Details)
                            VALUES (@Id, @UserId, @Username, @Action, @ResourceType, @ResourceId, @ResourceName, @Details)",
                new
                {
                    Id = DatabasePersistence.ToDbId(conn, Guid.NewGuid().ToString()),
                    UserId = DatabasePersistence.ToDbIdOrNull(conn, userId),
                    Username = username,
                    Action = action,
                    ResourceType = resourceType,
                    ResourceId = DatabasePersistence.ToDbIdOrNull(conn, resourceId),
                    ResourceName = resourceName,
                    Details = details
                });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AuditLogger: failed to write log entry: {ex.Message}");
        }
    }

    // Paginated, newest-first read for the admin Audit Log panel. Optional filters narrow by
    // actor and/or resource type; both are plain equality (no fuzzy search) since this is an
    // operational log, not a search index.
    public static List<Newtonsoft.Json.Linq.JObject> List(int limit, int offset, string userId = null, string resourceType = null)
    {
        using var conn = DatabasePersistence.CreateConnection();
        if (conn == null) return new List<Newtonsoft.Json.Linq.JObject>();
        conn.Open();

        var clauses = new List<string>();
        if (!string.IsNullOrEmpty(userId)) clauses.Add("UserId = @UserId");
        if (!string.IsNullOrEmpty(resourceType)) clauses.Add("ResourceType = @ResourceType");
        var where = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";

        var rows = conn.Query($@"SELECT * FROM AuditLog {where} ORDER BY CreatedAt DESC",
            new
            {
                UserId = string.IsNullOrEmpty(userId) ? null : DatabasePersistence.ToDbId(conn, userId),
                ResourceType = resourceType
            });

        return rows.Skip(offset).Take(limit)
            .Select(r => Newtonsoft.Json.Linq.JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(r)))
            .Cast<Newtonsoft.Json.Linq.JObject>().ToList();
    }
}
