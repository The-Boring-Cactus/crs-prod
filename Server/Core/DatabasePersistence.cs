using System.Data.Common;
using Dapper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Server.Core;

/// <summary>
/// Provides entity CRUD persistence against the user-configured database (MSSQL, Postgres, MySQL).
/// Replaces the former RocksDB-based LoadEntityData/SaveEntityData pattern.
/// </summary>
public static class DatabasePersistence
{
    /// <summary>
    /// Creates a new DbConnection from the setup config.
    /// Returns null if the system is not yet configured.
    /// </summary>
    public static DbConnection CreateConnection()
    {
        var config = SetupConfig.Load();
        if (!config.IsConfigured || config.Database == null)
            return null;

        var cs = config.Database.GetConnectionString();
        return config.Database.Type?.ToLower() switch
        {
            "mssql" => new Microsoft.Data.SqlClient.SqlConnection(cs),
            "postgresql" => new Npgsql.NpgsqlConnection(cs),
            "mysql" => new MySqlConnector.MySqlConnection(cs),
            "oracle" => CreateOracleConnection(cs),
            _ => null
        };
    }

    // Reflection-based Oracle loader — avoids direct type reference so the Oracle
    // assembly is only loaded when an Oracle connection is actually requested.
    private static DbConnection CreateOracleConnection(string cs)
    {
        try
        {
            var asm = System.Reflection.Assembly.Load("Oracle.ManagedDataAccess");
            var type = asm.GetType("Oracle.ManagedDataAccess.Client.OracleConnection", throwOnError: true)!;
            return (DbConnection)Activator.CreateInstance(type, cs)!;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Oracle.ManagedDataAccess assembly failed to load: " + ex.Message +
                ". Ensure Oracle.ManagedDataAccess.Core NuGet package is deployed.", ex);
        }
    }

    // Checks by type name so no direct Oracle assembly reference is needed.
    internal static bool IsOracleConnection(DbConnection conn)
        => conn?.GetType().FullName?.StartsWith("Oracle.") == true;

    // Converts a string entity id to the database's expected parameter type: MySQL/Oracle
    // store ids as plain strings, everything else uses native Guid columns. Internal (not
    // private) so AuditLogger can reuse it for its own inserts.
    internal static object ToDbId(DbConnection conn, string id) =>
        (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? id : Guid.Parse(id);

    internal static object ToDbIdOrNull(DbConnection conn, string id) =>
        string.IsNullOrEmpty(id) ? null : ToDbId(conn, id);

    // ── Access control (ResourceGrants) ──────────────────────────────────
    // A grant is a (ResourceType, ResourceId, GranteeUserId) row giving a specific user
    // "view" or "edit" access to something they don't own. ResourceType is always one of
    // this app's table names ('Projects', 'Dashboards', 'SqlScripts', ...) -- a compile-time
    // constant supplied by calling code, never user input, so it's safe to interpolate
    // directly into SQL below.
    //
    // Project membership (ResourceType = 'Projects') cascades: being granted access to a
    // project also grants access to everything inside it (its dashboards, scripts, data
    // models, and non-global database connections), via the second half of
    // GrantVisibilityClause. Direct per-resource grants exist independently of project
    // membership, for sharing a single dashboard/script with someone outside the project.
    private const string ProjectResourceType = "Projects";

    private static string ProjectVisibilityClause(string projectIdColumn = "ProjectId") => $@"
        OR ({projectIdColumn} IS NOT NULL AND {projectIdColumn} IN (SELECT ResourceId FROM ResourceGrants WHERE ResourceType = '{ProjectResourceType}' AND GranteeUserId = @UserId))";

    private static string GrantVisibilityClause(string resourceType, string idColumn = "Id", string projectIdColumn = "ProjectId") => $@"
        OR {idColumn} IN (SELECT ResourceId FROM ResourceGrants WHERE ResourceType = '{resourceType}' AND GranteeUserId = @UserId)
        {ProjectVisibilityClause(projectIdColumn)}";

    public static void SaveResourceGrant(string resourceType, string resourceId, string granteeUserId, string permission, string grantedByUserId)
    {
        using var conn = CreateConnection();
        if (conn == null) return;

        conn.Open();
        object dbResourceId = ToDbId(conn, resourceId);
        object dbGranteeId = ToDbId(conn, granteeUserId);
        object dbGrantedBy = ToDbId(conn, grantedByUserId);

        // Upsert via delete+insert, matching this file's convention elsewhere (e.g. SaveScript).
        conn.Execute("DELETE FROM ResourceGrants WHERE ResourceType = @ResourceType AND ResourceId = @ResourceId AND GranteeUserId = @GranteeUserId",
            new { ResourceType = resourceType, ResourceId = dbResourceId, GranteeUserId = dbGranteeId });
        conn.Execute(@"INSERT INTO ResourceGrants (Id, ResourceType, ResourceId, GranteeUserId, Permission, GrantedBy)
                        VALUES (@Id, @ResourceType, @ResourceId, @GranteeUserId, @Permission, @GrantedBy)",
            new
            {
                Id = ToDbId(conn, Guid.NewGuid().ToString()),
                ResourceType = resourceType,
                ResourceId = dbResourceId,
                GranteeUserId = dbGranteeId,
                Permission = permission == "edit" ? "edit" : "view",
                GrantedBy = dbGrantedBy
            });
    }

    public static void RevokeResourceGrant(string resourceType, string resourceId, string granteeUserId)
    {
        using var conn = CreateConnection();
        if (conn == null) return;

        conn.Open();
        conn.Execute("DELETE FROM ResourceGrants WHERE ResourceType = @ResourceType AND ResourceId = @ResourceId AND GranteeUserId = @GranteeUserId",
            new { ResourceType = resourceType, ResourceId = ToDbId(conn, resourceId), GranteeUserId = ToDbId(conn, granteeUserId) });
    }

    // Grants on one resource, joined with Users so the Share dialog can show a name instead
    // of a raw id.
    public static List<JObject> LoadResourceGrants(string resourceType, string resourceId)
    {
        using var conn = CreateConnection();
        if (conn == null) return new List<JObject>();

        conn.Open();
        var rows = conn.Query(@"SELECT g.GranteeUserId, g.Permission, g.CreatedAt, u.Username, u.FullName, u.Email
                                 FROM ResourceGrants g JOIN Users u ON u.Id = g.GranteeUserId
                                 WHERE g.ResourceType = @ResourceType AND g.ResourceId = @ResourceId",
            new { ResourceType = resourceType, ResourceId = ToDbId(conn, resourceId) });
        return rows.Select(r => JObject.Parse(JsonConvert.SerializeObject(r))).Cast<JObject>().ToList();
    }

    // Owning UserId (and, if the table has one, ProjectId) for a single row -- used to check
    // edit permission on a Save/Delete call before falling back to ResourceGrants.
    public static (string ownerUserId, string projectId) GetResourceOwner(string tableName, string id)
    {
        using var conn = CreateConnection();
        if (conn == null) return (null, null);

        conn.Open();
        var row = conn.QueryFirstOrDefault($"SELECT * FROM {tableName} WHERE Id = @Id", new { Id = ToDbId(conn, id) });
        if (row == null) return (null, null);

        var obj = JObject.Parse(JsonConvert.SerializeObject(row));
        var ownerId = obj["userid"]?.ToString() ?? obj["userId"]?.ToString() ?? obj["UserId"]?.ToString();
        var projId = obj["projectid"]?.ToString() ?? obj["projectId"]?.ToString() ?? obj["ProjectId"]?.ToString();
        return (ownerId, string.IsNullOrEmpty(projId) ? null : projId);
    }

    // True if userId may edit a row it doesn't own: an admin, a direct 'edit' grant on the
    // resource itself, or an 'edit' grant on the project it belongs to.
    public static bool HasEditGrant(string userId, string resourceType, string resourceId, string projectId)
    {
        using var conn = CreateConnection();
        if (conn == null) return false;

        conn.Open();
        var dbUserId = ToDbId(conn, userId);
        var count = conn.QueryFirstOrDefault<int>($@"
            SELECT COUNT(*) FROM ResourceGrants
            WHERE Permission = 'edit' AND GranteeUserId = @UserId
              AND ((ResourceType = @ResourceType AND ResourceId = @ResourceId)
                   {(string.IsNullOrEmpty(projectId) ? "" : $"OR (ResourceType = '{ProjectResourceType}' AND ResourceId = @ProjectId)")})",
            new { UserId = dbUserId, ResourceType = resourceType, ResourceId = ToDbId(conn, resourceId), ProjectId = string.IsNullOrEmpty(projectId) ? null : ToDbId(conn, projectId) });
        return count > 0;
    }

    // Resolves who to grant access to from what the Share dialog's "add a person" field
    // holds -- a username or an email, either exact. Returns null if nobody matches.
    public static JObject FindUserByUsernameOrEmail(string usernameOrEmail)
    {
        using var conn = CreateConnection();
        if (conn == null) return null;

        conn.Open();
        var row = conn.QueryFirstOrDefault(
            "SELECT Id, Username, FullName, Email FROM Users WHERE Username = @Q OR Email = @Q",
            new { Q = usernameOrEmail });
        return row == null ? null : JObject.Parse(JsonConvert.SerializeObject(row));
    }

    // ── Generic JSON entity operations ──────────────────────────────────
    // Each "entity table" stores rows as JSON blobs in a dedicated table,
    // keyed by (UserId, Id).  The tables were created during setup.

    // ── Scripts (SqlScripts + CodeScripts combined) ─────────────────────

    public static List<JObject> LoadScripts(string userId, string language, string projectId = null)
    {
        using var conn = CreateConnection();
        if (conn == null) return new List<JObject>();

        conn.Open();
        string tableName = language == "csharp" ? "CodeScripts" : "SqlScripts";
        object dbUserId = ToDbId(conn, userId);

        // Direct per-resource sharing only exists for SqlScripts today (SqlEditor's Share
        // button); CodeScripts visibility is still ownership + project-membership only.
        var grantClause = tableName == "SqlScripts" ? GrantVisibilityClause(tableName) : ProjectVisibilityClause();

        // Bind @ProjectId only when a filter is actually requested: passing a typeless
        // null (DBNull.Value boxed as object) leaves Npgsql unable to infer the parameter's
        // data type ("42P08: could not determine data type of parameter"), so the query
        // must simply omit the parameter/clause instead of relying on "@ProjectId IS NULL".
        IEnumerable<dynamic> rows;
        if (string.IsNullOrEmpty(projectId))
        {
            rows = conn.Query($@"SELECT * FROM {tableName} WHERE (UserId = @UserId {grantClause})",
                new { UserId = dbUserId });
        }
        else
        {
            object dbProjId = ToDbId(conn, projectId);
            rows = conn.Query($@"SELECT * FROM {tableName}
                WHERE (UserId = @UserId {grantClause})
                  AND ProjectId = @ProjectId",
                new { UserId = dbUserId, ProjectId = dbProjId });
        }
        return rows.Select(r => JObject.Parse(JsonConvert.SerializeObject(r))).Cast<JObject>().ToList();
    }

    public static void SaveScript(string userId, JObject scriptObj, string language)
    {
        using var conn = CreateConnection();
        if (conn == null) return;

        conn.Open();
        var id = scriptObj["id"]?.ToString() ?? scriptObj["Id"]?.ToString();
        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString();
            scriptObj["id"] = id;
        }

        var name = scriptObj["name"]?.ToString() ?? scriptObj["Name"]?.ToString() ?? "Untitled";
        var code = scriptObj["code"]?.ToString() ?? scriptObj["Code"]?.ToString() ?? "";
        var dbConnId = scriptObj["database"]?.ToString() ?? scriptObj["DatabaseConnectionId"]?.ToString() ?? "";
        var projIdStr = scriptObj["projectId"]?.ToString() ?? scriptObj["ProjectId"]?.ToString();

        object dbId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? id : Guid.Parse(id);
        object dbUserId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? userId : Guid.Parse(userId);
        object dbProjId = string.IsNullOrEmpty(projIdStr) ? null :
            ((conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? (object)projIdStr : Guid.Parse(projIdStr));

        if (language == "csharp")
        {
            conn.Execute(@"DELETE FROM CodeScripts WHERE Id = @Id AND UserId = @UserId",
                new { Id = dbId, UserId = dbUserId });
            conn.Execute(@"INSERT INTO CodeScripts (Id, UserId, Name, Language, Code, ProjectId)
                          VALUES (@Id, @UserId, @Name, @Language, @Code, @ProjectId)",
                new { Id = dbId, UserId = dbUserId, Name = name, Language = language, Code = code, ProjectId = dbProjId });
        }
        else
        {
            var visualizationStr = scriptObj["visualization"]?.ToString() ?? scriptObj["Visualization"]?.ToString();

            // A plain code/query save from SqlEditor never carries a "schedule" key --
            // it only ever gets set via UpdateScriptSchedule. Since this method deletes
            // and reinserts the row, look up the current value first so an ordinary save
            // doesn't silently wipe out a configured schedule.
            var scheduleStr = scriptObj.ContainsKey("schedule") || scriptObj.ContainsKey("Schedule")
                ? (scriptObj["schedule"]?.ToString() ?? scriptObj["Schedule"]?.ToString())
                : conn.QueryFirstOrDefault<string>("SELECT Schedule FROM SqlScripts WHERE Id = @Id AND UserId = @UserId",
                    new { Id = dbId, UserId = dbUserId });

            conn.Execute(@"DELETE FROM SqlScripts WHERE Id = @Id AND UserId = @UserId",
                new { Id = dbId, UserId = dbUserId });
            conn.Execute(@"INSERT INTO SqlScripts (Id, UserId, Name, Language, Code, DatabaseConnectionId, ProjectId, Visualization, Schedule)
                          VALUES (@Id, @UserId, @Name, @Language, @Code, @DatabaseConnectionId, @ProjectId, @Visualization, @Schedule)",
                new { Id = dbId, UserId = dbUserId, Name = name, Language = language, Code = code, DatabaseConnectionId = dbConnId, ProjectId = dbProjId, Visualization = visualizationStr, Schedule = scheduleStr });
        }
    }

    // Updates only the Schedule column, independent of SaveScript's full delete+reinsert --
    // used by the "Schedule Delivery" dialog so toggling/editing a schedule never touches
    // (or risks clobbering) the script's own code/visualization in the same round trip.
    public static void UpdateScriptSchedule(string userId, string id, string scheduleJson)
    {
        using var conn = CreateConnection();
        if (conn == null) return;

        conn.Open();
        object dbId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? id : Guid.Parse(id);
        object dbUserId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? userId : Guid.Parse(userId);

        conn.Execute("UPDATE SqlScripts SET Schedule = @Schedule WHERE Id = @Id AND UserId = @UserId",
            new { Schedule = scheduleJson, Id = dbId, UserId = dbUserId });
    }

    // Cross-user scan for the background delivery worker, which has to find every script
    // with an active schedule regardless of who owns it (mirrors LoadAllEntities's role
    // for ScheduledRefreshService).
    public static List<JObject> LoadAllScheduledSqlScripts()
    {
        using var conn = CreateConnection();
        if (conn == null) return new List<JObject>();

        conn.Open();
        var rows = conn.Query("SELECT * FROM SqlScripts WHERE Schedule IS NOT NULL");
        return rows.Select(r => JObject.Parse(JsonConvert.SerializeObject(r))).Cast<JObject>().ToList();
    }

    public static void DeleteScript(string userId, string id, string language)
    {
        using var conn = CreateConnection();
        if (conn == null) return;

        conn.Open();
        object dbId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? id : Guid.Parse(id);
        object dbUserId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? userId : Guid.Parse(userId);

        string tableName = language == "csharp" ? "CodeScripts" : "SqlScripts";
        conn.Execute($"DELETE FROM {tableName} WHERE Id = @Id AND UserId = @UserId",
            new { Id = dbId, UserId = dbUserId });
    }

    // ── DatabaseConnections ─────────────────────────────────────────────

    public static List<JObject> LoadDatabaseConnections(string userId, string projectId = null)
    {
        using var conn = CreateConnection();
        if (conn == null) return new List<JObject>();

        conn.Open();
        object dbUserId = ToDbId(conn, userId);
        // Project members see the project's connections too, alongside their own and any
        // marked IsGlobal -- there's no direct per-connection sharing (only project-wide).
        var projectClause = ProjectVisibilityClause();

        IEnumerable<dynamic> rows;
        if (!string.IsNullOrEmpty(projectId))
        {
            object dbProjId = ToDbId(conn, projectId);
            rows = conn.Query($@"SELECT * FROM DatabaseConnections
                WHERE (UserId = @UserId AND ProjectId = @ProjectId) OR (UserId = @UserId AND ProjectId IS NULL) OR IsGlobal = @True {projectClause}",
                new { UserId = dbUserId, ProjectId = dbProjId, True = true });
        }
        else
        {
            rows = conn.Query($@"SELECT * FROM DatabaseConnections WHERE UserId = @UserId OR IsGlobal = @True {projectClause}",
                new { UserId = dbUserId, True = true });
        }
        return rows.Select(r => JObject.Parse(JsonConvert.SerializeObject(r))).Cast<JObject>().ToList();
    }

    public static void SaveDatabaseConnection(string userId, JObject connObj)
    {
        using var conn = CreateConnection();
        if (conn == null) return;

        conn.Open();
        var id = connObj["id"]?.ToString() ?? connObj["Id"]?.ToString();
        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString();
            connObj["id"] = id;
        }

        object dbId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? id : Guid.Parse(id);
        object dbUserId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? userId : Guid.Parse(userId);

        var projIdStr = connObj["projectId"]?.ToString() ?? connObj["ProjectId"]?.ToString();
        object dbProjId = string.IsNullOrEmpty(projIdStr) ? null :
            ((conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? (object)projIdStr : Guid.Parse(projIdStr));

        conn.Execute("DELETE FROM DatabaseConnections WHERE Id = @Id AND UserId = @UserId",
            new { Id = dbId, UserId = dbUserId });

        conn.Execute(@"INSERT INTO DatabaseConnections
            (Id, UserId, Name, Type, Host, Port, DatabaseName, Username, Password, ConnectionString, IsGlobal, SharedWith, ProjectId)
            VALUES (@Id, @UserId, @Name, @Type, @Host, @Port, @DatabaseName, @Username, @Password, @ConnectionString, @IsGlobal, @SharedWith, @ProjectId)",
            new
            {
                Id = dbId,
                UserId = dbUserId,
                Name = connObj["name"]?.ToString() ?? connObj["Name"]?.ToString() ?? "",
                Type = connObj["type"]?.ToString() ?? connObj["Type"]?.ToString() ?? "",
                Host = connObj["host"]?.ToString() ?? connObj["Host"]?.ToString() ?? "",
                Port = (int)(connObj["port"] ?? connObj["Port"] ?? 0),
                DatabaseName = connObj["database"]?.ToString() ?? connObj["DatabaseName"]?.ToString() ?? "",
                Username = connObj["username"]?.ToString() ?? connObj["Username"]?.ToString() ?? "",
                Password = connObj["password"]?.ToString() ?? connObj["Password"]?.ToString() ?? "",
                ConnectionString = connObj["connectionString"]?.ToString() ?? connObj["ConnectionString"]?.ToString() ?? "",
                IsGlobal = (bool)(connObj["isGlobal"] ?? connObj["IsGlobal"] ?? false),
                SharedWith = connObj["sharedWith"]?.ToString() ?? connObj["SharedWith"]?.ToString() ?? "",
                ProjectId = dbProjId
            });
    }

    public static void DeleteDatabaseConnection(string userId, string id)
    {
        using var conn = CreateConnection();
        if (conn == null) return;

        conn.Open();
        object dbId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? id : Guid.Parse(id);
        object dbUserId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? userId : Guid.Parse(userId);
        
        conn.Execute("DELETE FROM DatabaseConnections WHERE Id = @Id AND UserId = @UserId",
            new { Id = dbId, UserId = dbUserId });
    }

    // ── Generic JSON-blob entities (Datasets, Excels, Dashboards, Reports) ──────

    private static readonly HashSet<string> ShareableTables = new() { "Dashboards", "Reports" };

    // Tables where a single row can be shared directly with a specific user (via the Share
    // dialog), independent of project membership. Everything else in this generic-entity
    // family (Datasets, DataModels) is still visible to project members, just not shareable
    // one row at a time.
    private static readonly HashSet<string> GrantableTables = new() { "Dashboards" };

    public static List<JObject> LoadEntities(string userId, string tableName, string projectId = null)
    {
        using var conn = CreateConnection();
        if (conn == null) return new List<JObject>();

        conn.Open();
        object dbUserId = ToDbId(conn, userId);
        var grantClause = GrantableTables.Contains(tableName) ? GrantVisibilityClause(tableName) : ProjectVisibilityClause();

        // Bind @ProjectId only when a filter is actually requested: passing a typeless
        // null (DBNull.Value boxed as object) leaves Npgsql unable to infer the parameter's
        // data type ("42P08: could not determine data type of parameter"), so the query
        // must simply omit the parameter/clause instead of relying on "@ProjectId IS NULL".
        IEnumerable<dynamic> rows;
        if (string.IsNullOrEmpty(projectId))
        {
            rows = conn.Query($@"SELECT * FROM {tableName} WHERE (UserId = @UserId {grantClause})",
                new { UserId = dbUserId });
        }
        else
        {
            object dbProjId = ToDbId(conn, projectId);
            rows = conn.Query($@"SELECT * FROM {tableName}
                WHERE (UserId = @UserId {grantClause})
                  AND ProjectId = @ProjectId",
                new { UserId = dbUserId, ProjectId = dbProjId });
        }
        return rows.Select(r => JObject.Parse(JsonConvert.SerializeObject(r))).Cast<JObject>().ToList();
    }

    // Cross-user read used by ScheduledRefreshService, which needs to scan every
    // saved dashboard (not just one user's) to find widgets due for a background refresh.
    public static List<JObject> LoadAllEntities(string tableName)
    {
        using var conn = CreateConnection();
        if (conn == null) return new List<JObject>();

        conn.Open();
        var rows = conn.Query($"SELECT * FROM {tableName}");
        return rows.Select(r => JObject.Parse(JsonConvert.SerializeObject(r))).Cast<JObject>().ToList();
    }

    public static void SaveEntity(string userId, string tableName, JObject obj)
    {
        using var conn = CreateConnection();
        if (conn == null) return;

        conn.Open();
        var id = obj["id"]?.ToString() ?? obj["Id"]?.ToString();
        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString();
            obj["id"] = id;
        }

        var name = obj["name"]?.ToString() ?? obj["Name"]?.ToString() ?? "Untitled";
        var config = JsonConvert.SerializeObject(obj);

        object dbId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? id : Guid.Parse(id);
        object dbUserId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? userId : Guid.Parse(userId);

        var projIdStr = obj["projectId"]?.ToString() ?? obj["ProjectId"]?.ToString();
        object dbProjId = string.IsNullOrEmpty(projIdStr) ? null :
            ((conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? (object)projIdStr : Guid.Parse(projIdStr));

        conn.Execute($"DELETE FROM {tableName} WHERE Id = @Id AND UserId = @UserId",
            new { Id = dbId, UserId = dbUserId });

        if (ShareableTables.Contains(tableName))
        {
            var isPublic = (obj["isPublic"] ?? obj["IsPublic"])?.Value<bool>() ?? false;
            var shareToken = (obj["shareToken"] ?? obj["ShareToken"])?.ToString();
            conn.Execute($@"INSERT INTO {tableName} (Id, UserId, Name, Config, IsPublic, ShareToken, ProjectId)
                            VALUES (@Id, @UserId, @Name, @Config, @IsPublic, @ShareToken, @ProjectId)",
                new { Id = dbId, UserId = dbUserId, Name = name, Config = config, IsPublic = isPublic, ShareToken = shareToken, ProjectId = dbProjId });
        }
        else
        {
            conn.Execute($@"INSERT INTO {tableName} (Id, UserId, Name, Config, ProjectId)
                            VALUES (@Id, @UserId, @Name, @Config, @ProjectId)",
                new { Id = dbId, UserId = dbUserId, Name = name, Config = config, ProjectId = dbProjId });
        }
    }

    public static void DeleteEntity(string userId, string tableName, string id)
    {
        using var conn = CreateConnection();
        if (conn == null) return;

        conn.Open();
        object dbId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? id : Guid.Parse(id);
        object dbUserId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? userId : Guid.Parse(userId);

        conn.Execute($"DELETE FROM {tableName} WHERE Id = @Id AND UserId = @UserId",
            new { Id = dbId, UserId = dbUserId });
    }

    // ── Public sharing ──────────────────────────────────────────────────

    public static string GenerateShareToken(string userId, string tableName, string id)
    {
        using var conn = CreateConnection();
        if (conn == null) return null;

        conn.Open();
        object dbId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? id : Guid.Parse(id);
        object dbUserId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? userId : Guid.Parse(userId);

        var token = Guid.NewGuid().ToString("N")[..16];
        conn.Execute($"UPDATE {tableName} SET ShareToken = @Token, IsPublic = @True WHERE Id = @Id AND UserId = @UserId",
            new { Token = token, True = true, Id = dbId, UserId = dbUserId });
        return token;
    }

    public static void RevokeShareToken(string userId, string tableName, string id)
    {
        using var conn = CreateConnection();
        if (conn == null) return;

        conn.Open();
        object dbId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? id : Guid.Parse(id);
        object dbUserId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? userId : Guid.Parse(userId);

        conn.Execute($"UPDATE {tableName} SET ShareToken = NULL, IsPublic = @False WHERE Id = @Id AND UserId = @UserId",
            new { False = false, Id = dbId, UserId = dbUserId });
    }

    public static JObject LoadEntityByShareToken(string tableName, string shareToken)
    {
        using var conn = CreateConnection();
        if (conn == null) return null;

        conn.Open();
        var row = conn.QueryFirstOrDefault($"SELECT * FROM {tableName} WHERE ShareToken = @Token",
            new { Token = shareToken });
        if (row == null) return null;
        return JObject.Parse(JsonConvert.SerializeObject(row));
    }

    // ── Reports (generic entity, same shape as Dashboards) ──────────────

    public static List<JObject> LoadReports(string userId)
        => LoadEntities(userId, "Reports");

    public static void SaveReport(string userId, JObject reportObj)
        => SaveEntity(userId, "Reports", reportObj);

    public static void DeleteReport(string userId, string id)
        => DeleteEntity(userId, "Reports", id);

    // ── Projects ────────────────────────────────────────────────────────

    public static void RunMigrations()
    {
        var config = SetupConfig.Load();
        if (!config.IsConfigured || config.Database == null) return;

        var migrationSQL = config.Database.GetMigrationSQL();
        if (string.IsNullOrWhiteSpace(migrationSQL)) return;

        bool isOracle = config.Database.Type?.ToLower() == "oracle";
        string[] statements;
        if (isOracle)
            statements = migrationSQL.Split(new[] { "\n/" }, StringSplitOptions.RemoveEmptyEntries);
        else
            statements = migrationSQL.Split(';', StringSplitOptions.RemoveEmptyEntries);

        using var conn = CreateConnection();
        if (conn == null) return;
        conn.Open();

        foreach (var stmt in statements)
        {
            var trimmed = stmt.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            try { conn.Execute(trimmed); }
            catch (Exception ex)
            {
                Console.WriteLine($"[Migration] Statement skipped (may already be applied): {ex.Message}");
            }
        }
    }

    public static List<JObject> LoadProjects(string userId)
    {
        using var conn = CreateConnection();
        if (conn == null) return new List<JObject>();

        conn.Open();
        object dbUserId = ToDbId(conn, userId);
        // A project has no ProjectId column of its own (it IS the project), so membership
        // is a direct grant on the project's own Id rather than ProjectVisibilityClause.
        var rows = conn.Query($@"SELECT * FROM Projects
            WHERE UserId = @UserId
               OR Id IN (SELECT ResourceId FROM ResourceGrants WHERE ResourceType = '{ProjectResourceType}' AND GranteeUserId = @UserId)
            ORDER BY CreatedAt",
            new { UserId = dbUserId });
        return rows.Select(r => JObject.Parse(JsonConvert.SerializeObject(r))).Cast<JObject>().ToList();
    }

    public static void SaveProject(string userId, JObject projectObj)
    {
        using var conn = CreateConnection();
        if (conn == null) return;

        conn.Open();
        var id = projectObj["id"]?.ToString() ?? projectObj["Id"]?.ToString();
        if (string.IsNullOrEmpty(id)) { id = Guid.NewGuid().ToString(); projectObj["id"] = id; }

        object dbId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? id : Guid.Parse(id);
        object dbUserId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? userId : Guid.Parse(userId);

        conn.Execute("DELETE FROM Projects WHERE Id = @Id AND UserId = @UserId", new { Id = dbId, UserId = dbUserId });
        conn.Execute("INSERT INTO Projects (Id, UserId, Name, Description) VALUES (@Id, @UserId, @Name, @Description)",
            new
            {
                Id = dbId,
                UserId = dbUserId,
                Name = projectObj["name"]?.ToString() ?? projectObj["Name"]?.ToString() ?? "Untitled",
                Description = projectObj["description"]?.ToString() ?? projectObj["Description"]?.ToString() ?? ""
            });
    }

    public static void DeleteProject(string userId, string id)
    {
        using var conn = CreateConnection();
        if (conn == null) return;

        conn.Open();
        object dbId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? id : Guid.Parse(id);
        object dbUserId = (conn is MySqlConnector.MySqlConnection || IsOracleConnection(conn)) ? userId : Guid.Parse(userId);
        conn.Execute("DELETE FROM Projects WHERE Id = @Id AND UserId = @UserId", new { Id = dbId, UserId = dbUserId });
    }

    // ── Variables ────────────────────────────────────────────────────────────────

    public static List<JObject> LoadVariables(string userId, string projectId = null)
    {
        using var conn = CreateConnection();
        if (conn == null) return new List<JObject>();
        conn.Open();
        // Variables table uses VARCHAR(36) in all databases — pass strings, not Guid objects.
        IEnumerable<dynamic> rows;
        if (!string.IsNullOrEmpty(projectId))
        {
            rows = conn.Query("SELECT * FROM Variables WHERE UserId = @UserId AND (ProjectId = @ProjectId OR ProjectId IS NULL)",
                new { UserId = userId, ProjectId = projectId });
        }
        else
        {
            rows = conn.Query("SELECT * FROM Variables WHERE UserId = @UserId", new { UserId = userId });
        }
        return rows.Select(r => JObject.Parse(JsonConvert.SerializeObject(r))).Cast<JObject>().ToList();
    }

    public static void SaveVariable(string userId, JObject varObj)
    {
        using var conn = CreateConnection();
        if (conn == null) return;
        conn.Open();
        var id = varObj["id"]?.ToString() ?? varObj["Id"]?.ToString();
        if (string.IsNullOrEmpty(id)) { id = Guid.NewGuid().ToString(); varObj["id"] = id; }
        // Variables table uses VARCHAR(36) in all databases — pass strings, not Guid objects.
        var projIdStr = varObj["projectId"]?.ToString() ?? varObj["ProjectId"]?.ToString();
        conn.Execute("DELETE FROM Variables WHERE Id = @Id AND UserId = @UserId", new { Id = id, UserId = userId });
        conn.Execute(@"INSERT INTO Variables (Id, UserId, ProjectId, Name, Label, Type, DefaultValue, DropdownSource, DropdownValues, DropdownQuery, DropdownConnectionId)
                       VALUES (@Id, @UserId, @ProjectId, @Name, @Label, @Type, @DefaultValue, @DropdownSource, @DropdownValues, @DropdownQuery, @DropdownConnectionId)",
            new {
                Id = id, UserId = userId, ProjectId = string.IsNullOrEmpty(projIdStr) ? null : projIdStr,
                Name = varObj["name"]?.ToString() ?? "var",
                Label = varObj["label"]?.ToString(),
                Type = varObj["type"]?.ToString() ?? "input",
                DefaultValue = varObj["defaultValue"]?.ToString(),
                DropdownSource = varObj["dropdownSource"]?.ToString(),
                DropdownValues = varObj["dropdownValues"]?.ToString(),
                DropdownQuery = varObj["dropdownQuery"]?.ToString(),
                DropdownConnectionId = varObj["dropdownConnectionId"]?.ToString()
            });
    }

    public static void DeleteVariable(string userId, string id)
    {
        using var conn = CreateConnection();
        if (conn == null) return;
        conn.Open();
        // Variables table uses VARCHAR(36) in all databases — pass strings, not Guid objects.
        conn.Execute("DELETE FROM Variables WHERE Id = @Id AND UserId = @UserId", new { Id = id, UserId = userId });
    }
}
