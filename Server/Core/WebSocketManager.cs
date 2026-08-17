using FunctEngine;
using GenHTTP.Api.Protocol;
using GenHTTP.Modules.Websockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Server.Helpers;
using System.Collections.Concurrent;

namespace Server.Core;

public class WebSocketManager
{
    private readonly ConcurrentDictionary<string, ConnectionInfo> _connections;
    private readonly IAuthService _authService;
    private readonly DataSourceManager _dataSourceManager;

    public WebSocketManager(IAuthService authService, DataSourceManager dataSourceManager)
    {
        _connections = new ConcurrentDictionary<string, ConnectionInfo>();
        _authService = authService;
        _dataSourceManager = dataSourceManager;
    }

    public async Task HandleWebSocketAsync(IWebsocketConnection socket)
    {
        var connectionId = Guid.NewGuid().ToString();
        var cancellationTokenSource = new CancellationTokenSource();

        var connectionInfo = new ConnectionInfo
        {
            ConnectionId = connectionId,
            ConnectedAt = DateTime.UtcNow,
            WebSocket = socket,
            CancellationTokenSource = cancellationTokenSource,
            WebSocketMessageClient = new WebSocketMessageClient(socket),
            interpreter = new CodeEngine(connectionId)
        };

        connectionInfo.WebSocketMessageClient.AuthenticationMessageReceived += AuthenticationMessage;
        connectionInfo.WebSocketMessageClient.CommandMessageReceived += CommandMessage;
        connectionInfo.WebSocketMessageClient.TextMessageReceived += TextMessage;
        connectionInfo.WebSocketMessageClient.NotificationMessageReceived += NotificationMessage;
        connectionInfo.WebSocketMessageClient.ErrorMessageReceived += ErrorMessage;
        connectionInfo.WebSocketMessageClient.DataMessageReceived += DataMessage;
        connectionInfo.WebSocketMessageClient.HeartbeatMessageReceived += HeartbeatMessage;
        connectionInfo.WebSocketMessageClient.ErrorOccurred += ErrorOccurred;
        connectionInfo.interpreter.StatusUpdate += InterpreterStatusUpdate;
        connectionInfo.interpreter.OutputEmitted += InterpreterOutputEmitted;
        connectionInfo.interpreter.LoadExternalDll("MathFunctions.dll");
        connectionInfo.interpreter.LoadExternalDll("DateTimeFunctions.dll");
        connectionInfo.interpreter.LoadExternalDll("DoeFunctions.dll");
        connectionInfo.interpreter.LoadExternalDll("FinancialFunctions.dll");
        connectionInfo.interpreter.LoadExternalDll("StringUtilities.dll");
        connectionInfo.interpreter.LoadExternalDll("DataTableFunctions.dll");
        connectionInfo.interpreter.LoadExternalDll("TimeSeriesFunctions.dll");
        connectionInfo.interpreter.LoadExternalDll("NonParametricFunctions.dll");
        connectionInfo.interpreter.LoadExternalDll("DistributionFunctions.dll");

        if (!_connections.TryAdd(connectionId, connectionInfo))
        {
            socket.Close();
            return;
        }

        try
        {
            await SendMessageAsync(connectionInfo, new HeartbeatMessage(), socket);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WebSocket error for connection {connectionId}: {ex.Message}");
            _connections.TryRemove(connectionId, out _);
            cancellationTokenSource.Cancel();
            cancellationTokenSource.Dispose();
        }
    }

    private void InterpreterStatusUpdate(object sender, StatusString e)
    {
        ConnectionInfo connectionInfo = _connections.FirstOrDefault(s => s.Value.interpreter == sender).Value;
        if (connectionInfo == null) return;

        var notification = new NotificationMessage
        {
            Category = "Debug",
            Content = e.status,
            Title = "Execution Debug"
        };

        using var _ = SendMessageAsync(connectionInfo, notification, connectionInfo.WebSocket);
    }

    private void InterpreterOutputEmitted(object sender, OutputEmittedEventArgs e)
    {
        ConnectionInfo connectionInfo = _connections.FirstOrDefault(s => s.Value.interpreter == sender).Value;
        if (connectionInfo == null) return;

        var dataMsg = new DataMessage
        {
            DataType = e.OutputType,
            Payload = e.Payload
        };

        using var _ = SendMessageAsync(connectionInfo, dataMsg, connectionInfo.WebSocket);
    }

    private void HeartbeatMessage(object sender, MessageReceivedEventArgs e)
    {
        IWebsocketConnection socket = e.WebSocket;
        ConnectionInfo connectionInfo = _connections.FirstOrDefault(s => s.Value.WebSocket == socket).Value;
        using var _ = SendMessageAsync(connectionInfo, new HeartbeatMessage(), socket);
    }

    private void DataMessage(object sender, MessageReceivedEventArgs e)
    {
        // Client-to-server data messages are logged; no action needed currently
        Console.WriteLine($"DataMessage received from client");
    }

    private void ErrorMessage(object sender, MessageReceivedEventArgs e)
    {
        var errMsg = e.Message as ErrorMessage;
        Console.WriteLine($"ErrorMessage received: {errMsg?.ErrorDescription}");
    }

    private void NotificationMessage(object sender, MessageReceivedEventArgs e)
    {
        // Client-to-server notifications are logged; no action needed currently
        Console.WriteLine($"NotificationMessage received from client");
    }

    private void TextMessage(object sender, MessageReceivedEventArgs e)
    {
        // Client-to-server text messages are logged; no action needed currently
        Console.WriteLine($"TextMessage received from client");
    }

    private void RegisterUserDatabaseConnections(string userId)
    {
        var conns = DatabasePersistence.LoadDatabaseConnections(userId);
        foreach (var c in conns)
        {
            var id = c["id"]?.ToString() ?? c["Id"]?.ToString();
            var type = (c["type"]?.ToString() ?? c["Type"]?.ToString() ?? "").ToLower();
            var connectionString = c["connectionString"]?.ToString() ?? c["ConnectionString"]?.ToString();
            if (string.IsNullOrWhiteSpace(connectionString)) connectionString = null;

            if (string.IsNullOrEmpty(id)) continue;

            var host = c["host"]?.ToString() ?? c["Host"]?.ToString();
            var db = c["databasename"]?.ToString() ?? c["DatabaseName"]?.ToString();
            var user = c["username"]?.ToString() ?? c["Username"]?.ToString();
            var pass = c["password"]?.ToString() ?? c["Password"]?.ToString();
            int.TryParse((c["port"]?.ToString() ?? c["Port"]?.ToString()), out int port);

            try
            {
                System.Data.IDbConnection conn = type switch
                {
                    "mssql" => new Microsoft.Data.SqlClient.SqlConnection(
                        connectionString ?? $"Server={host},{port};Database={db};User Id={user};Password={pass};TrustServerCertificate=True;"),
                    "postgresql" => new Npgsql.NpgsqlConnection(
                        connectionString ?? $"Host={host};Port={port};Database={db};Username={user};Password={pass};"),
                    "mysql" => new MySqlConnector.MySqlConnection(
                        connectionString ?? $"Server={host};Port={port};Database={db};Uid={user};Pwd={pass};"),
                    _ => null
                };

                if (conn != null)
                    _dataSourceManager.AddConnection(id, conn);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error registering connection {id}: {ex.Message}");
            }
        }
    }

    private void RegisterConnectionsToInterpreter(ConnectionInfo connectionInfo, string userId)
    {
        var conns = DatabasePersistence.LoadDatabaseConnections(userId);
        foreach (var c in conns)
        {
            var id = c["id"]?.ToString() ?? c["Id"]?.ToString();
            var type = (c["type"]?.ToString() ?? c["Type"]?.ToString() ?? "").ToLower();
            var connectionString = c["connectionString"]?.ToString() ?? c["ConnectionString"]?.ToString();
            if (string.IsNullOrWhiteSpace(connectionString)) connectionString = null;
            var name = c["name"]?.ToString() ?? c["Name"]?.ToString() ?? id;

            if (string.IsNullOrEmpty(id)) continue;

            var host = c["host"]?.ToString() ?? c["Host"]?.ToString();
            var db = c["databasename"]?.ToString() ?? c["DatabaseName"]?.ToString();
            var user = c["username"]?.ToString() ?? c["Username"]?.ToString();
            var pass = c["password"]?.ToString() ?? c["Password"]?.ToString();
            int.TryParse((c["port"]?.ToString() ?? c["Port"]?.ToString()), out int port);

            try
            {
                System.Data.IDbConnection conn = type switch
                {
                    "mssql" => new Microsoft.Data.SqlClient.SqlConnection(
                        connectionString ?? $"Server={host},{port};Database={db};User Id={user};Password={pass};TrustServerCertificate=True;"),
                    "postgresql" => new Npgsql.NpgsqlConnection(
                        connectionString ?? $"Host={host};Port={port};Database={db};Username={user};Password={pass};"),
                    "mysql" => new MySqlConnector.MySqlConnection(
                        connectionString ?? $"Server={host};Port={port};Database={db};Uid={user};Pwd={pass};"),
                    _ => null
                };

                if (conn != null)
                {
                    conn.Open();

                    // Register by both ID and name so scripts can reference either
                    connectionInfo.interpreter.RegisterDatabaseConnection(id, conn);
                    if (!string.IsNullOrEmpty(name) && name != id)
                        connectionInfo.interpreter.RegisterDatabaseConnection(name, conn);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error registering interpreter connection {id}: {ex.Message}");
            }
        }
    }

    private void RegisterProjectFunctions(ConnectionInfo connectionInfo, string userId, string projectId)
    {
        // ExecuteScript(scriptName) — runs a saved SQL script by name on its stored connection
        connectionInfo.interpreter.RegisterExternalFunction("ExecuteScript", args =>
        {
            if (args.Length == 0) return new List<object>();
            string scriptName = args[0]?.ToString() ?? "";

            var scripts = DatabasePersistence.LoadScripts(userId, "sql", projectId);
            var script = scripts.FirstOrDefault(s =>
                string.Equals(s["name"]?.ToString() ?? s["Name"]?.ToString() ?? "",
                              scriptName, StringComparison.OrdinalIgnoreCase));

            if (script == null)
            {
                connectionInfo.interpreter.PrintCore($"ExecuteScript: script '{scriptName}' not found");
                return new List<object>();
            }

            string sqlCode = script["code"]?.ToString() ?? script["Code"]?.ToString() ?? "";
            string dbId = script["databaseconnectionid"]?.ToString()
                       ?? script["DatabaseConnectionId"]?.ToString()
                       ?? script["database"]?.ToString()
                       ?? script["Database"]?.ToString() ?? "";

            if (string.IsNullOrEmpty(sqlCode))
            {
                connectionInfo.interpreter.PrintCore($"ExecuteScript: script '{scriptName}' has no code");
                return new List<object>();
            }
            if (string.IsNullOrEmpty(dbId))
            {
                connectionInfo.interpreter.PrintCore($"ExecuteScript: script '{scriptName}' has no database connection");
                return new List<object>();
            }

            return connectionInfo.interpreter.ExecuteDatabaseQuery(dbId, sqlCode);
        });

        // ReadDataset(name) — returns rows from a saved Dataset as List<Dictionary>
        connectionInfo.interpreter.RegisterExternalFunction("ReadDataset", args =>
        {
            if (args.Length == 0) return new List<object>();
            string name = args[0]?.ToString() ?? "";
            return LoadProjectDataRows(connectionInfo, userId, projectId, name, "ReadDataset");
        });

        // ReadSpreadsheet(name) — returns rows from a saved Excel spreadsheet as List<Dictionary>
        connectionInfo.interpreter.RegisterExternalFunction("ReadSpreadsheet", args =>
        {
            if (args.Length == 0) return new List<object>();
            string name = args[0]?.ToString() ?? "";
            return LoadProjectDataRows(connectionInfo, userId, projectId, name, "ReadSpreadsheet");
        });

        // ReadDataModel(name, requestJson) — runs a query built by DataModelQueryBuilder
        // against a saved Data Model and returns the rows as List<Dictionary>. requestJson
        // is the same {fields, filters, groupBy, orderBy, limit} shape the RunDataModelQuery
        // websocket command accepts, e.g.:
        //   ReadDataModel("Sales Model", '{"fields":[{"table":"c","column":"name"},{"table":"o","column":"amount","aggregate":"sum","alias":"total"}],"groupBy":[{"table":"c","column":"name"}],"filters":[{"table":"o","column":"status","op":"=","value":"paid"}],"limit":100}')
        connectionInfo.interpreter.RegisterExternalFunction("ReadDataModel", args =>
        {
            if (args.Length == 0) return new List<object>();
            string name = args[0]?.ToString() ?? "";
            string requestJson = args.Length > 1 ? args[1]?.ToString() ?? "{}" : "{}";
            return LoadDataModelRows(connectionInfo, userId, name, requestJson);
        });
    }

    // Finds a saved DataModel by name, builds its SQL via DataModelQueryBuilder for the
    // given request, and runs it on the model's own connection -- the script/dataset
    // equivalent of the "RunDataModelQuery" websocket command.
    private List<object> LoadDataModelRows(ConnectionInfo connectionInfo, string userId, string name, string requestJson)
    {
        var entities = DatabasePersistence.LoadEntities(userId, "DataModels");
        var entity = entities.FirstOrDefault(e =>
            string.Equals(e["name"]?.ToString() ?? e["Name"]?.ToString() ?? "",
                          name, StringComparison.OrdinalIgnoreCase));

        if (entity == null)
        {
            connectionInfo.interpreter.PrintCore($"ReadDataModel: '{name}' not found");
            return new List<object>();
        }

        try
        {
            var configJson = entity["config"]?.ToString() ?? entity["Config"]?.ToString() ?? "{}";
            var model = DataModelQueryBuilder.ParseModel(configJson);
            var request = DataModelQueryBuilder.ParseRequest(JObject.Parse(requestJson));
            var (sql, sqlParams) = DataModelQueryBuilder.Build(model, request);

            var result = _dataSourceManager.ExecuteQueryAsync(model.ConnectionId, sql, sqlParams).GetAwaiter().GetResult();
            var rows = new List<object>();
            foreach (var row in result) rows.Add(RowToDictionary(row));
            return rows;
        }
        catch (Exception ex)
        {
            connectionInfo.interpreter.PrintCore($"ReadDataModel: error querying '{name}': {ex.Message}");
            return new List<object>();
        }
    }

    private static List<object> LoadProjectDataRows(ConnectionInfo connectionInfo, string userId, string projectId, string name, string caller)
    {
        var entities = DatabasePersistence.LoadEntities(userId, "Datasets", projectId);
        var entity = entities.FirstOrDefault(e =>
            string.Equals(e["name"]?.ToString() ?? e["Name"]?.ToString() ?? "",
                          name, StringComparison.OrdinalIgnoreCase));

        if (entity == null)
        {
            connectionInfo.interpreter.PrintCore($"{caller}: '{name}' not found");
            return new List<object>();
        }

        string configStr = entity["config"]?.ToString() ?? entity["Config"]?.ToString() ?? "";
        if (string.IsNullOrEmpty(configStr)) return new List<object>();

        try
        {
            var config = Newtonsoft.Json.Linq.JObject.Parse(configStr);
            var dataArray = config["data"] as Newtonsoft.Json.Linq.JArray;
            if (dataArray == null) return new List<object>();

            return dataArray.Select(row =>
                (object)(row is Newtonsoft.Json.Linq.JObject rowObj
                    ? rowObj.ToObject<Dictionary<string, object>>()
                    : new Dictionary<string, object>())
            ).ToList();
        }
        catch (Exception ex)
        {
            connectionInfo.interpreter.PrintCore($"{caller}: error parsing data for '{name}': {ex.Message}");
            return new List<object>();
        }
    }

    // Role hierarchy: viewer (0) < user/editor (1) < admin (2). Unrecognized/legacy
    // role strings default to "user" so existing accounts aren't accidentally locked out.
    private static int RoleLevel(string roles) => roles switch
    {
        "admin" => 2,
        "viewer" => 0,
        _ => 1
    };

    // Resolves who a write to tableName/id should be persisted as, given who's actually
    // making the call. DatabasePersistence's Save*/Delete* methods are all scoped by an
    // owning UserId, so a collaborator with only an edit grant (not ownership) still writes
    // under the row's real owner -- this is the one place that gets decided, rather than
    // teaching every persistence method about grants.
    // Returns null if the caller has neither ownership, an edit grant, nor admin rights.
    private static string ResolveWriteOwner(string tableName, string id, string callerId, string callerRole)
    {
        if (string.IsNullOrEmpty(id)) return callerId; // new row: caller becomes the owner

        var (ownerId, projectId) = DatabasePersistence.GetResourceOwner(tableName, id);
        if (string.IsNullOrEmpty(ownerId)) return callerId; // row not found -- let the normal path no-op/404

        if (ownerId == callerId) return callerId;
        if (RoleLevel(callerRole) >= 2) return ownerId; // admin
        if (DatabasePersistence.HasEditGrant(callerId, tableName, id, projectId)) return ownerId;
        return null;
    }

    // ResourceType values accepted by ShareResource/RevokeResourceGrant/ListResourceGrants --
    // each doubles as the real table name (see DatabasePersistence.GetResourceOwner).
    // 'Projects' covers project membership; 'Dashboards'/'SqlScripts' cover direct,
    // single-resource sharing.
    private static readonly HashSet<string> GrantableResourceTypes = new(StringComparer.Ordinal)
    {
        "Projects", "Dashboards", "SqlScripts"
    };

    // Only the owner (or an admin) may grant/revoke/view who has access to a resource --
    // an edit grant lets someone change the resource's content, not decide who else can.
    private static bool CanManageSharing(string resourceType, string resourceId, string callerId, string callerRole)
    {
        if (RoleLevel(callerRole) >= 2) return true;
        var (ownerId, _) = DatabasePersistence.GetResourceOwner(resourceType, resourceId);
        return ownerId == callerId;
    }

    // Commands that create/modify/delete/share saved content -- blocked for "viewer".
    private static readonly HashSet<string> EditorOrAdminCommands = new(StringComparer.Ordinal)
    {
        "SaveScript", "DeleteScript", "SaveScriptSchedule", "RunScriptScheduleNow",
        "SaveDatabaseConnection", "DeleteDatabaseConnection", "TestDatabaseConnection",
        "SaveExcel", "DeleteExcel",
        "SaveDashboard", "DeleteDashboard", "ShareDashboard", "EmailShareLink",
        "SaveProject", "DeleteProject",
        "SaveVariable", "DeleteVariable",
        "SaveReport", "DeleteReport", "ShareReport",
        "SaveDataModel", "DeleteDataModel", "ListTables",
        "ShareResource", "RevokeResourceGrant", "ListResourceGrants"
    };

    // Instance-wide settings and user administration -- admin only.
    private static readonly HashSet<string> AdminOnlyCommands = new(StringComparer.Ordinal)
    {
        "GetSmtpConfig", "UpdateSmtpConfig",
        "ListUsers", "UpdateUserRole", "SetUserActive",
        "ListAuditLog"
    };

    private void CommandMessage(object sender, MessageReceivedEventArgs e)
    {
        IWebsocketConnection socket = e.WebSocket;
        ConnectionInfo connectionInfo = _connections.FirstOrDefault(s => s.Value.WebSocket == socket).Value;
        if (connectionInfo == null) return;

        string uuid = !string.IsNullOrEmpty(connectionInfo.UserId) ? connectionInfo.UserId : connectionInfo.ConnectionId;
        // Console.WriteLine($"{uuid}: {e.Message.ToString()}");
        var cmdMessage = e.Message as CommandMessage;
        
        if (cmdMessage == null) return;

        var parameters = cmdMessage.Parameters;
        // Console.WriteLine( JsonConvert.SerializeObject(parameters));
        var response = new ResponseMessage
        {
            RequestId = cmdMessage.Id,
            Status = MessageStatus.Success,
            ErrorMessage = ""
        };

        // Centralized role gate: "viewer" can view/run everything already saved
        // (ExecuteCs/ExecuteSql render existing dashboards) but cannot create, edit,
        // delete, or share content; a handful of instance-wide settings are admin-only.
        if (AdminOnlyCommands.Contains(cmdMessage.Command) && RoleLevel(connectionInfo.Roles) < 2)
        {
            response.Status = MessageStatus.Error;
            response.ErrorMessage = "This action requires administrator access.";
            using var _adminGate = SendMessageAsync(connectionInfo, response, socket);
            return;
        }
        if (EditorOrAdminCommands.Contains(cmdMessage.Command) && RoleLevel(connectionInfo.Roles) < 1)
        {
            response.Status = MessageStatus.Error;
            response.ErrorMessage = "Viewers cannot perform this action.";
            using var _viewerGate = SendMessageAsync(connectionInfo, response, socket);
            return;
        }

        try
        {
            switch (cmdMessage.Command)
            {
                case "ExecuteCs":
                    if (parameters.ContainsKey("code"))
                    {
                        string code = parameters["code"].ToString();
                        string execProjectId = parameters.ContainsKey("projectId") ? parameters["projectId"]?.ToString() : null;

                        // Extract variable values passed by the client { varName: value }
                        var varDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        if (parameters.ContainsKey("variables") && parameters["variables"] is Newtonsoft.Json.Linq.JObject varsObj)
                        {
                            foreach (var kv in varsObj)
                                varDict[kv.Key] = kv.Value?.ToString() ?? "";
                        }

                        RegisterConnectionsToInterpreter(connectionInfo, uuid);
                        RegisterProjectFunctions(connectionInfo, uuid, execProjectId);

                        // GetVar('name') lets scripts read the current variable values
                        connectionInfo.interpreter.RegisterExternalFunction("GetVar", args =>
                        {
                            if (args.Length == 0) return "";
                            var name = args[0]?.ToString() ?? "";
                            return varDict.TryGetValue(name, out var v) ? v : "";
                        });

                        // Run on a background thread so long scripts don't block the WebSocket pump
                        Task.Run(() =>
                        {
                            try
                            {
                                connectionInfo.interpreter.Execute(code);

                                // Send completion notification
                                var done = new NotificationMessage
                                {
                                    Category = "ExecutionComplete",
                                    Content = "Script execution finished.",
                                    Title = "Done"
                                };
                                using var _1 = SendMessageAsync(connectionInfo, done, socket);
                            }
                            catch (Exception ex)
                            {
                                var errResp = new ResponseMessage
                                {
                                    RequestId = cmdMessage.Id,
                                    Status = MessageStatus.Error,
                                    ErrorMessage = ex.Message
                                };
                                using var _2 = SendMessageAsync(connectionInfo, errResp, socket);
                            }
                        });
                    }
                    response.Data = new { message = "Execution started" };
                    break;

                case "ExecuteSql":
                    if (parameters.ContainsKey("database") && parameters.ContainsKey("code"))
                    {
                        string dbId = parameters["database"].ToString();
                        string sql = parameters["code"].ToString();
                        bool forceRefresh = parameters.ContainsKey("forceRefresh") && Convert.ToBoolean(parameters["forceRefresh"]);

                        try
                        {
                            var result = _dataSourceManager.ExecuteQueryAsync(dbId, sql, forceRefresh: forceRefresh).GetAwaiter().GetResult();

                            var rows = new List<Dictionary<string, object>>();
                            var columns = new List<object>();
                            bool colsExtracted = false;

                            foreach (var row in result)
                            {
                                // ReportsCache round-trips through System.Text.Json, turning
                                // Dapper DapperRow (IDictionary) into JsonElement on cache hit.
                                // Normalise both cases to a plain dictionary with CLR values.
                                var rowDict = RowToDictionary(row);

                                if (!colsExtracted)
                                {
                                    foreach (var key in rowDict.Keys)
                                        columns.Add(new { field = key, header = key });
                                    colsExtracted = true;
                                }
                                rows.Add(rowDict);
                            }

                            response.Data = new { rows, columns };
                        }
                        catch (Exception dbEx)
                        {
                            response.Status = MessageStatus.Error;
                            response.ErrorMessage = dbEx.Message;
                        }
                    }
                    else
                    {
                        response.Status = MessageStatus.Error;
                        response.ErrorMessage = "Missing database or code parameter";
                    }
                    break;

                case "SaveScript":
                    if (parameters.ContainsKey("script"))
                    {
                        var scriptObj = JObject.FromObject(parameters["script"]);
                        var lang = scriptObj["language"]?.ToString() ?? "sql";
                        var scriptTable = lang == "csharp" ? "CodeScripts" : "SqlScripts";
                        var scriptOwner = ResolveWriteOwner(scriptTable, scriptObj["id"]?.ToString(), uuid, connectionInfo.Roles);
                        if (scriptOwner == null)
                        {
                            response.Status = MessageStatus.Error;
                            response.ErrorMessage = "You don't have permission to edit this script.";
                            break;
                        }
                        DatabasePersistence.SaveScript(scriptOwner, scriptObj, lang);
                        AuditLogger.Log(uuid, connectionInfo.Username, "save", scriptTable, scriptObj["id"]?.ToString(), scriptObj["name"]?.ToString());
                    }
                    break;

                case "LoadScripts":
                {
                    string scriptLang = parameters.ContainsKey("language") ? parameters["language"].ToString() : "";
                    var pid = parameters.ContainsKey("projectId") ? parameters["projectId"]?.ToString() : null;
                    response.Data = DatabasePersistence.LoadScripts(uuid, scriptLang, pid);
                    break;
                }

                case "DeleteScript":
                    if (parameters.ContainsKey("id"))
                    {
                        var id = parameters["id"].ToString();
                        var lang2 = parameters.ContainsKey("language") ? parameters["language"].ToString() : "sql";
                        var deleteScriptTable = lang2 == "csharp" ? "CodeScripts" : "SqlScripts";
                        var deleteScriptOwner = ResolveWriteOwner(deleteScriptTable, id, uuid, connectionInfo.Roles);
                        if (deleteScriptOwner == null)
                        {
                            response.Status = MessageStatus.Error;
                            response.ErrorMessage = "You don't have permission to delete this script.";
                            break;
                        }
                        DatabasePersistence.DeleteScript(deleteScriptOwner, id, lang2);
                        AuditLogger.Log(uuid, connectionInfo.Username, "delete", deleteScriptTable, id);
                    }
                    break;

                // Persists a SQL Editor script's scheduled-delivery config independently of
                // SaveScript, which does a full delete+reinsert of the row -- keeping this
                // separate means editing code and editing the schedule can't race or clobber
                // each other in the same round trip.
                case "SaveScriptSchedule":
                    if (parameters.ContainsKey("id") && parameters.ContainsKey("schedule"))
                    {
                        var scheduleJson = JObject.FromObject(parameters["schedule"]).ToString(Formatting.None);
                        DatabasePersistence.UpdateScriptSchedule(uuid, parameters["id"].ToString(), scheduleJson);
                        response.Data = new { success = true };
                    }
                    break;

                // Runs a script's query and emails it right now, using the schedule object
                // passed from the dialog (which may be a draft the user hasn't saved yet)
                // rather than whatever is currently persisted -- lets "send test now" reflect
                // in-progress edits to recipients/frequency.
                case "RunScriptScheduleNow":
                {
                    if (!parameters.ContainsKey("id"))
                    {
                        response.Status = MessageStatus.Error;
                        response.ErrorMessage = "Missing script id";
                        break;
                    }
                    var scriptId = parameters["id"].ToString();
                    var scriptForRun = DatabasePersistence.LoadScripts(uuid, "sql")
                        .FirstOrDefault(s => (s["id"]?.ToString() ?? s["Id"]?.ToString()) == scriptId);
                    if (scriptForRun == null)
                    {
                        response.Status = MessageStatus.Error;
                        response.ErrorMessage = "Script not found";
                        break;
                    }
                    JObject scheduleForRun;
                    if (parameters.ContainsKey("schedule"))
                    {
                        scheduleForRun = JObject.FromObject(parameters["schedule"]);
                    }
                    else
                    {
                        var storedSchedule = scriptForRun["schedule"]?.ToString() ?? scriptForRun["Schedule"]?.ToString();
                        scheduleForRun = string.IsNullOrWhiteSpace(storedSchedule) ? new JObject() : JObject.Parse(storedSchedule);
                    }

                    var (success, message) = ReportScheduleWorker.RunAndDeliver(scriptForRun, scheduleForRun, persist: false);
                    if (!success)
                    {
                        response.Status = MessageStatus.Error;
                        response.ErrorMessage = message;
                    }
                    else
                    {
                        response.Data = new { message };
                    }
                    break;
                }

                case "SaveDatabaseConnection":
                    if (parameters.ContainsKey("connection"))
                    {
                        var connObj = JObject.FromObject(parameters["connection"]);
                        DatabasePersistence.SaveDatabaseConnection(uuid, connObj);
                        RegisterUserDatabaseConnections(uuid);
                        AuditLogger.Log(uuid, connectionInfo.Username, "save", "DatabaseConnections", connObj["id"]?.ToString(), connObj["name"]?.ToString());
                    }
                    break;

                case "LoadDatabaseConnections":
                {
                    var pid = parameters.ContainsKey("projectId") ? parameters["projectId"]?.ToString() : null;
                    response.Data = DatabasePersistence.LoadDatabaseConnections(uuid, pid);
                    break;
                }

                case "DeleteDatabaseConnection":
                    if (parameters.ContainsKey("id"))
                    {
                        var id = parameters["id"].ToString();
                        DatabasePersistence.DeleteDatabaseConnection(uuid, id);
                        AuditLogger.Log(uuid, connectionInfo.Username, "delete", "DatabaseConnections", id);
                    }
                    break;

                case "TestDatabaseConnection":
                    if (parameters.ContainsKey("connection"))
                    {
                        var connObj = JObject.FromObject(parameters["connection"]);
                        var testResult = TestConnection(connObj);
                        response.Data = testResult;
                    }
                    else
                    {
                        response.Data = new { success = false, message = "No connection parameters provided" };
                    }
                    break;

                case "SaveExcel":
                    if (parameters.ContainsKey("excel"))
                    {
                        var exObj = JObject.FromObject(parameters["excel"]);
                        DatabasePersistence.SaveEntity(uuid, "Datasets", exObj);
                    }
                    break;

                case "LoadExcels":
                {
                    var pid = parameters.ContainsKey("projectId") ? parameters["projectId"]?.ToString() : null;
                    response.Data = DatabasePersistence.LoadEntities(uuid, "Datasets", pid);
                    break;
                }

                case "DeleteExcel":
                    if (parameters.ContainsKey("id"))
                    {
                        var id = parameters["id"].ToString();
                        DatabasePersistence.DeleteEntity(uuid, "Datasets", id);
                    }
                    break;

                case "SaveDashboard":
                    if (parameters.ContainsKey("dashboard"))
                    {
                        var dashObj = JObject.FromObject(parameters["dashboard"]);
                        var dashOwner = ResolveWriteOwner("Dashboards", dashObj["id"]?.ToString(), uuid, connectionInfo.Roles);
                        if (dashOwner == null)
                        {
                            response.Status = MessageStatus.Error;
                            response.ErrorMessage = "You don't have permission to edit this dashboard.";
                            break;
                        }
                        DatabasePersistence.SaveEntity(dashOwner, "Dashboards", dashObj);
                        response.Data = dashObj;
                        AuditLogger.Log(uuid, connectionInfo.Username, "save", "Dashboards", dashObj["id"]?.ToString(), dashObj["name"]?.ToString());
                    }
                    break;

                case "LoadDashboards":
                {
                    var pid = parameters.ContainsKey("projectId") ? parameters["projectId"]?.ToString() : null;
                    response.Data = DatabasePersistence.LoadEntities(uuid, "Dashboards", pid);
                    break;
                }

                case "DeleteDashboard":
                    if (parameters.ContainsKey("id"))
                    {
                        var id = parameters["id"].ToString();
                        var deleteDashOwner = ResolveWriteOwner("Dashboards", id, uuid, connectionInfo.Roles);
                        if (deleteDashOwner == null)
                        {
                            response.Status = MessageStatus.Error;
                            response.ErrorMessage = "You don't have permission to delete this dashboard.";
                            break;
                        }
                        DatabasePersistence.DeleteEntity(deleteDashOwner, "Dashboards", id);
                        AuditLogger.Log(uuid, connectionInfo.Username, "delete", "Dashboards", id);
                    }
                    break;

                case "ShareDashboard":
                {
                    var id = parameters.ContainsKey("id") ? parameters["id"].ToString() : null;
                    var enable = parameters.ContainsKey("enable") && Convert.ToBoolean(parameters["enable"]);
                    if (!string.IsNullOrEmpty(id))
                    {
                        if (enable)
                        {
                            var token = DatabasePersistence.GenerateShareToken(uuid, "Dashboards", id);
                            response.Data = new { shareToken = token };
                        }
                        else
                        {
                            DatabasePersistence.RevokeShareToken(uuid, "Dashboards", id);
                            response.Data = new { shareToken = (string)null, shareUrl = (string)null };
                        }
                    }
                    break;
                }

                // Emails an already-generated public share link (Dashboards or Reports)
                // to a recipient chosen from the Share dialog. The frontend supplies the
                // link/title it's already displaying rather than this looking the entity
                // back up — the caller must already be authenticated to reach this command.
                case "EmailShareLink":
                {
                    var to = parameters.ContainsKey("to") ? parameters["to"]?.ToString() : null;
                    var shareUrl = parameters.ContainsKey("shareUrl") ? parameters["shareUrl"]?.ToString() : null;
                    var title = parameters.ContainsKey("title") ? parameters["title"]?.ToString() : "Dashboard";
                    var note = parameters.ContainsKey("message") ? parameters["message"]?.ToString() : null;

                    if (string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(shareUrl))
                    {
                        response.Status = MessageStatus.Error;
                        response.ErrorMessage = "Missing recipient email or share link";
                        break;
                    }

                    var noteHtml = string.IsNullOrWhiteSpace(note) ? "" : $"<p>{System.Net.WebUtility.HtmlEncode(note)}</p>";
                    var html = $@"
                        {noteHtml}
                        <p>You've been invited to view the dashboard ""{System.Net.WebUtility.HtmlEncode(title)}"" on CRS Reporter:</p>
                        <p><a href=""{shareUrl}"">{shareUrl}</a></p>";

                    var (sent, sendMessage) = EmailService.SendEmailAsync(to, $"{title} — shared with you", html).GetAwaiter().GetResult();
                    if (!sent)
                    {
                        response.Status = MessageStatus.Error;
                        response.ErrorMessage = sendMessage;
                    }
                    break;
                }

                case "LoadProjects":
                    response.Data = DatabasePersistence.LoadProjects(uuid);
                    break;

                case "SaveProject":
                    if (parameters.ContainsKey("project"))
                    {
                        var projObj = JObject.FromObject(parameters["project"]);
                        DatabasePersistence.SaveProject(uuid, projObj);
                        response.Data = new { id = projObj["id"]?.ToString() };
                        AuditLogger.Log(uuid, connectionInfo.Username, "save", "Projects", projObj["id"]?.ToString(), projObj["name"]?.ToString());
                    }
                    break;

                case "DeleteProject":
                    if (parameters.ContainsKey("id"))
                    {
                        var deleteProjId = parameters["id"].ToString();
                        DatabasePersistence.DeleteProject(uuid, deleteProjId);
                        AuditLogger.Log(uuid, connectionInfo.Username, "delete", "Projects", deleteProjId);
                    }
                    break;

                case "LoadVariables":
                {
                    var pid = parameters.ContainsKey("projectId") ? parameters["projectId"]?.ToString() : null;
                    response.Data = DatabasePersistence.LoadVariables(uuid, pid);
                    break;
                }

                case "SaveVariable":
                    if (parameters.ContainsKey("variable"))
                    {
                        var varObj = JObject.FromObject(parameters["variable"]);
                        DatabasePersistence.SaveVariable(uuid, varObj);
                        response.Data = new { id = varObj["id"]?.ToString() };
                    }
                    break;

                case "DeleteVariable":
                    if (parameters.ContainsKey("id"))
                        DatabasePersistence.DeleteVariable(uuid, parameters["id"].ToString());
                    break;

                // Reads the last server-side scheduled-refresh result for a SqlWidget, if
                // any -- a "viewing" primitive like ExecuteSql (not gated to editors), since
                // this is exactly what lets a viewer see recently-refreshed data. Returns
                // cached=false when nothing has been cached yet (widget has no refresh
                // interval configured, or the first background tick hasn't run yet); callers
                // should fall back to a live ExecuteSql in that case.
                case "GetWidgetCache":
                    if (parameters.ContainsKey("dashboardId") && parameters.ContainsKey("widgetId"))
                    {
                        var cached = ScheduledRefreshService.GetCached(parameters["dashboardId"].ToString(), parameters["widgetId"].ToString());
                        response.Data = cached == null
                            ? new { cached = false }
                            : new { cached = true, rows = cached.Rows, columns = cached.Columns, refreshedAt = cached.RefreshedAt, error = cached.Error };
                    }
                    break;

                case "ResolveDropdownQuery":
                    if (parameters.ContainsKey("database") && parameters.ContainsKey("query"))
                    {
                        string dbId2 = parameters["database"].ToString();
                        string qry = parameters["query"].ToString();
                        try
                        {
                            var qResult = _dataSourceManager.ExecuteQueryAsync(dbId2, qry).GetAwaiter().GetResult();
                            var opts = new List<string>();
                            foreach (var row in qResult)
                            {
                                Dictionary<string, object> rd = RowToDictionary(row);
                                if (rd.Count > 0) opts.Add(rd.Values.First()?.ToString() ?? "");
                            }
                            response.Data = opts;
                        }
                        catch (Exception qEx)
                        {
                            response.Status = MessageStatus.Error;
                            response.ErrorMessage = qEx.Message;
                        }
                    }
                    break;

                case "SaveDataModel":
                    if (parameters.ContainsKey("model"))
                    {
                        var modelObj = JObject.FromObject(parameters["model"]);
                        var modelOwner = ResolveWriteOwner("DataModels", modelObj["id"]?.ToString(), uuid, connectionInfo.Roles);
                        if (modelOwner == null)
                        {
                            response.Status = MessageStatus.Error;
                            response.ErrorMessage = "You don't have permission to edit this data model.";
                            break;
                        }
                        DatabasePersistence.SaveEntity(modelOwner, "DataModels", modelObj);
                        response.Data = modelObj;
                        AuditLogger.Log(uuid, connectionInfo.Username, "save", "DataModels", modelObj["id"]?.ToString(), modelObj["name"]?.ToString());
                    }
                    break;

                case "LoadDataModels":
                {
                    var pid = parameters.ContainsKey("projectId") ? parameters["projectId"]?.ToString() : null;
                    response.Data = DatabasePersistence.LoadEntities(uuid, "DataModels", pid);
                    break;
                }

                case "DeleteDataModel":
                    if (parameters.ContainsKey("id"))
                    {
                        var deleteModelId = parameters["id"].ToString();
                        var deleteModelOwner = ResolveWriteOwner("DataModels", deleteModelId, uuid, connectionInfo.Roles);
                        if (deleteModelOwner == null)
                        {
                            response.Status = MessageStatus.Error;
                            response.ErrorMessage = "You don't have permission to delete this data model.";
                            break;
                        }
                        DatabasePersistence.DeleteEntity(deleteModelOwner, "DataModels", deleteModelId);
                        AuditLogger.Log(uuid, connectionInfo.Username, "delete", "DataModels", deleteModelId);
                    }
                    break;

                // ── Access control: per-user sharing + project membership ──────────
                // ResourceType is always one of GrantableResourceTypes below, which double
                // as real table names ('Projects' rows ARE the project; 'Dashboards' and
                // 'SqlScripts' rows are the two kinds of content directly shareable today).
                // Only the resource's owner or an admin can grant/revoke/list who has
                // access -- an edit grant lets you change the resource's content, not decide
                // who else gets to.
                case "ShareResource":
                {
                    var resourceType = parameters.ContainsKey("resourceType") ? parameters["resourceType"]?.ToString() : null;
                    var resourceId = parameters.ContainsKey("resourceId") ? parameters["resourceId"]?.ToString() : null;
                    var granteeQuery = parameters.ContainsKey("grantee") ? parameters["grantee"]?.ToString()?.Trim() : null;
                    var permission = parameters.ContainsKey("permission") ? parameters["permission"]?.ToString() : "view";

                    if (!GrantableResourceTypes.Contains(resourceType) || string.IsNullOrEmpty(resourceId) || string.IsNullOrEmpty(granteeQuery))
                    {
                        response.Status = MessageStatus.Error;
                        response.ErrorMessage = "Missing or invalid resourceType/resourceId/grantee";
                        break;
                    }
                    if (!CanManageSharing(resourceType, resourceId, uuid, connectionInfo.Roles))
                    {
                        response.Status = MessageStatus.Error;
                        response.ErrorMessage = "Only the owner or an admin can share this.";
                        break;
                    }

                    var grantee = DatabasePersistence.FindUserByUsernameOrEmail(granteeQuery);
                    var granteeId = grantee?["Id"]?.ToString() ?? grantee?["id"]?.ToString();
                    if (string.IsNullOrEmpty(granteeId))
                    {
                        response.Status = MessageStatus.Error;
                        response.ErrorMessage = $"No user found matching \"{granteeQuery}\".";
                        break;
                    }

                    DatabasePersistence.SaveResourceGrant(resourceType, resourceId, granteeId, permission, uuid);
                    AuditLogger.Log(uuid, connectionInfo.Username, "share", resourceType, resourceId, details: $"granted {permission} to {grantee["Username"] ?? grantee["username"]}");
                    response.Data = new { success = true };
                    break;
                }

                case "RevokeResourceGrant":
                {
                    var resourceType = parameters.ContainsKey("resourceType") ? parameters["resourceType"]?.ToString() : null;
                    var resourceId = parameters.ContainsKey("resourceId") ? parameters["resourceId"]?.ToString() : null;
                    var granteeUserId = parameters.ContainsKey("granteeUserId") ? parameters["granteeUserId"]?.ToString() : null;

                    if (!GrantableResourceTypes.Contains(resourceType) || string.IsNullOrEmpty(resourceId) || string.IsNullOrEmpty(granteeUserId))
                    {
                        response.Status = MessageStatus.Error;
                        response.ErrorMessage = "Missing or invalid resourceType/resourceId/granteeUserId";
                        break;
                    }
                    if (!CanManageSharing(resourceType, resourceId, uuid, connectionInfo.Roles))
                    {
                        response.Status = MessageStatus.Error;
                        response.ErrorMessage = "Only the owner or an admin can manage sharing for this.";
                        break;
                    }

                    DatabasePersistence.RevokeResourceGrant(resourceType, resourceId, granteeUserId);
                    AuditLogger.Log(uuid, connectionInfo.Username, "unshare", resourceType, resourceId);
                    response.Data = new { success = true };
                    break;
                }

                case "ListResourceGrants":
                {
                    var resourceType = parameters.ContainsKey("resourceType") ? parameters["resourceType"]?.ToString() : null;
                    var resourceId = parameters.ContainsKey("resourceId") ? parameters["resourceId"]?.ToString() : null;

                    if (!GrantableResourceTypes.Contains(resourceType) || string.IsNullOrEmpty(resourceId))
                    {
                        response.Status = MessageStatus.Error;
                        response.ErrorMessage = "Missing or invalid resourceType/resourceId";
                        break;
                    }
                    if (!CanManageSharing(resourceType, resourceId, uuid, connectionInfo.Roles))
                    {
                        response.Status = MessageStatus.Error;
                        response.ErrorMessage = "Only the owner or an admin can view sharing for this.";
                        break;
                    }

                    response.Data = DatabasePersistence.LoadResourceGrants(resourceType, resourceId);
                    break;
                }

                // Admin-only, newest-first, optionally filtered by actor or resource type.
                case "ListAuditLog":
                {
                    var limit = parameters.ContainsKey("limit") && int.TryParse(parameters["limit"]?.ToString(), out var l) ? Math.Clamp(l, 1, 500) : 100;
                    var offset = parameters.ContainsKey("offset") && int.TryParse(parameters["offset"]?.ToString(), out var o) ? Math.Max(0, o) : 0;
                    var filterUserId = parameters.ContainsKey("userId") ? parameters["userId"]?.ToString() : null;
                    var filterResourceType = parameters.ContainsKey("resourceType") ? parameters["resourceType"]?.ToString() : null;
                    response.Data = AuditLogger.List(limit, offset, filterUserId, filterResourceType);
                    break;
                }

                // Introspects a saved DB connection's tables/columns so the Data Model
                // editor can offer them for building relationships -- editors/admins only
                // (gated via EditorOrAdminCommands), same trust level as authoring a query.
                case "ListTables":
                    if (parameters.ContainsKey("connectionId"))
                    {
                        var connId = parameters["connectionId"].ToString();
                        var conns = DatabasePersistence.LoadDatabaseConnections(uuid);
                        var connInfo = conns.FirstOrDefault(c => (c["id"]?.ToString() ?? c["Id"]?.ToString()) == connId);
                        var dbType = (connInfo?["type"]?.ToString() ?? connInfo?["Type"]?.ToString() ?? "").ToLower();

                        string introspectionSql = dbType switch
                        {
                            "postgresql" => "SELECT table_name, column_name, data_type FROM information_schema.columns WHERE table_schema='public' ORDER BY table_name, ordinal_position",
                            "mysql" => "SELECT table_name, column_name, data_type FROM information_schema.columns WHERE table_schema = DATABASE() ORDER BY table_name, ordinal_position",
                            "mssql" => "SELECT TABLE_NAME as table_name, COLUMN_NAME as column_name, DATA_TYPE as data_type FROM INFORMATION_SCHEMA.COLUMNS ORDER BY TABLE_NAME, ORDINAL_POSITION",
                            _ => null
                        };

                        if (introspectionSql == null)
                        {
                            response.Status = MessageStatus.Error;
                            response.ErrorMessage = "Unsupported or unknown connection type for schema introspection.";
                        }
                        else
                        {
                            try
                            {
                                var rows = _dataSourceManager.ExecuteQueryAsync(connId, introspectionSql).GetAwaiter().GetResult();
                                var tables = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
                                foreach (var row in rows)
                                {
                                    Dictionary<string, object> rd = RowToDictionary(row);
                                    var tableName = rd.TryGetValue("table_name", out object tn) ? tn?.ToString() : null;
                                    if (string.IsNullOrEmpty(tableName)) continue;
                                    if (!tables.ContainsKey(tableName)) tables[tableName] = new List<object>();
                                    tables[tableName].Add(new
                                    {
                                        columnName = rd.TryGetValue("column_name", out object cn) ? cn?.ToString() : null,
                                        dataType = rd.TryGetValue("data_type", out object dt) ? dt?.ToString() : null
                                    });
                                }
                                response.Data = tables.Select(kv => new { tableName = kv.Key, columns = kv.Value }).ToArray();
                            }
                            catch (Exception tEx)
                            {
                                response.Status = MessageStatus.Error;
                                response.ErrorMessage = tEx.Message;
                            }
                        }
                    }
                    break;

                // Runs a joined query against a saved Data Model -- this is the "viewing"
                // primitive dashboard widgets call (like ExecuteSql), so it is deliberately
                // NOT in EditorOrAdminCommands: a viewer opening a dashboard with a
                // Data-Model-backed widget must still be able to render it.
                case "RunDataModelQuery":
                    if (parameters.ContainsKey("modelId"))
                    {
                        var modelId = parameters["modelId"].ToString();
                        try
                        {
                            var modelEntities = DatabasePersistence.LoadEntities(uuid, "DataModels");
                            var modelEntity = modelEntities.FirstOrDefault(m => (m["id"]?.ToString() ?? m["Id"]?.ToString()) == modelId);
                            if (modelEntity == null)
                            {
                                response.Status = MessageStatus.Error;
                                response.ErrorMessage = "Data model not found.";
                                break;
                            }

                            var configJson = modelEntity["config"]?.ToString() ?? modelEntity["Config"]?.ToString() ?? modelEntity.ToString();
                            var model = DataModelQueryBuilder.ParseModel(configJson);
                            var paramsObj = JObject.FromObject(parameters);
                            var request = DataModelQueryBuilder.ParseRequest(paramsObj);
                            var (sql, sqlParams) = DataModelQueryBuilder.Build(model, request);

                            var result = _dataSourceManager.ExecuteQueryAsync(model.ConnectionId, sql, sqlParams).GetAwaiter().GetResult();
                            var flatRows = new List<Dictionary<string, object>>();
                            foreach (var row in result) flatRows.Add(RowToDictionary(row));

                            List<Dictionary<string, object>> rows;
                            var columns = new List<object>();
                            if (request.Pivot != null)
                            {
                                var (pivotRows, pivotColumns) = DataModelQueryBuilder.ReshapeForPivot(flatRows, request.Pivot);
                                rows = pivotRows;
                                foreach (var c in pivotColumns) columns.Add(new { field = c, header = c });
                            }
                            else
                            {
                                rows = flatRows;
                                if (rows.Count > 0)
                                    foreach (var key in rows[0].Keys) columns.Add(new { field = key, header = key });
                            }
                            response.Data = new { rows, columns, sql };
                        }
                        catch (Exception mEx)
                        {
                            response.Status = MessageStatus.Error;
                            response.ErrorMessage = mEx.Message;
                        }
                    }
                    break;

                case "LoadReports":
                    response.Data = DatabasePersistence.LoadReports(uuid);
                    break;

                case "SaveReport":
                    if (parameters.ContainsKey("report"))
                    {
                        var repObj = JObject.FromObject(parameters["report"]);
                        DatabasePersistence.SaveReport(uuid, repObj);
                        response.Data = new { id = repObj["id"]?.ToString() };
                    }
                    break;

                case "DeleteReport":
                    if (parameters.ContainsKey("id"))
                        DatabasePersistence.DeleteReport(uuid, parameters["id"].ToString());
                    break;

                case "ShareReport":
                {
                    var id = parameters.ContainsKey("id") ? parameters["id"].ToString() : null;
                    var enable = parameters.ContainsKey("enable") && Convert.ToBoolean(parameters["enable"]);
                    if (!string.IsNullOrEmpty(id))
                    {
                        if (enable)
                        {
                            var token = DatabasePersistence.GenerateShareToken(uuid, "Reports", id);
                            response.Data = new { shareToken = token };
                        }
                        else
                        {
                            DatabasePersistence.RevokeShareToken(uuid, "Reports", id);
                            response.Data = new { shareToken = (string)null };
                        }
                    }
                    break;
                }

                case "UpdateUserProfile":
                {
                    var displayName = parameters.ContainsKey("displayName") ? parameters["displayName"]?.ToString() : null;
                    if (!string.IsNullOrWhiteSpace(displayName))
                    {
                        var ok = _authService.UpdateUserProfileAsync(uuid, displayName).GetAwaiter().GetResult();
                        if (!ok) { response.Status = MessageStatus.Error; response.ErrorMessage = "Update failed"; }
                        else { response.Data = new { displayName }; }
                    }
                    break;
                }

                case "ChangePassword":
                {
                    var oldPw = parameters.ContainsKey("oldPassword") ? parameters["oldPassword"]?.ToString() : null;
                    var newPw = parameters.ContainsKey("newPassword") ? parameters["newPassword"]?.ToString() : null;
                    if (string.IsNullOrWhiteSpace(oldPw) || string.IsNullOrWhiteSpace(newPw))
                    {
                        response.Status = MessageStatus.Error;
                        response.ErrorMessage = "Both old and new passwords are required";
                    }
                    else
                    {
                        var ok = _authService.ChangePasswordAsync(uuid, oldPw, newPw).GetAwaiter().GetResult();
                        if (!ok) { response.Status = MessageStatus.Error; response.ErrorMessage = "Current password is incorrect"; }
                    }
                    break;
                }

                // SMTP is a system-wide setting (not per-user) — gated to admins only via
                // AdminOnlyCommands above. The stored password is never sent back to the
                // client; a blank Password field on save means "leave it unchanged".
                case "GetSmtpConfig":
                {
                    var cfg = SetupConfig.Load();
                    var smtp = cfg.Smtp ?? new SmtpConfig();
                    response.Data = new
                    {
                        host = smtp.Host,
                        port = smtp.Port,
                        fromAddress = smtp.FromAddress,
                        username = smtp.Username,
                        useSsl = smtp.UseSsl,
                        isConfigured = smtp.IsConfigured,
                        hasPassword = !string.IsNullOrEmpty(smtp.Password)
                    };
                    break;
                }

                case "UpdateSmtpConfig":
                {
                    var cfg = SetupConfig.Load();
                    var smtp = cfg.Smtp ?? new SmtpConfig();

                    smtp.Host = parameters.ContainsKey("host") ? parameters["host"]?.ToString() : smtp.Host;
                    smtp.FromAddress = parameters.ContainsKey("fromAddress") ? parameters["fromAddress"]?.ToString() : smtp.FromAddress;
                    smtp.Username = parameters.ContainsKey("username") ? parameters["username"]?.ToString() : smtp.Username;
                    smtp.UseSsl = parameters.ContainsKey("useSsl") && Convert.ToBoolean(parameters["useSsl"]);
                    if (parameters.ContainsKey("port") && int.TryParse(parameters["port"]?.ToString(), out var port))
                        smtp.Port = port;
                    // Only overwrite the stored password if a new one was actually typed.
                    var newSmtpPassword = parameters.ContainsKey("password") ? parameters["password"]?.ToString() : null;
                    if (!string.IsNullOrEmpty(newSmtpPassword))
                        smtp.Password = newSmtpPassword;

                    smtp.IsConfigured = !string.IsNullOrWhiteSpace(smtp.Host) && !string.IsNullOrWhiteSpace(smtp.FromAddress);
                    cfg.Smtp = smtp;
                    cfg.Save();

                    response.Data = new { success = true, isConfigured = smtp.IsConfigured };
                    break;
                }

                case "ListUsers":
                {
                    var users = _authService.GetAllUsersAsync().GetAwaiter().GetResult();
                    response.Data = users.Select(u => new
                    {
                        userId = u.UserId,
                        username = u.Username,
                        email = u.Email,
                        createdAt = u.CreatedAt,
                        isActive = u.IsActive,
                        roles = u.Roles
                    }).ToArray();
                    break;
                }

                case "UpdateUserRole":
                {
                    var targetUserId = parameters.ContainsKey("userId") ? parameters["userId"]?.ToString() : null;
                    var newRole = parameters.ContainsKey("role") ? parameters["role"]?.ToString() : null;
                    if (string.IsNullOrWhiteSpace(targetUserId) || string.IsNullOrWhiteSpace(newRole))
                    {
                        response.Status = MessageStatus.Error;
                        response.ErrorMessage = "userId and role are required";
                    }
                    else
                    {
                        var (ok, message) = _authService.UpdateUserRoleAsync(targetUserId, newRole).GetAwaiter().GetResult();
                        if (!ok) { response.Status = MessageStatus.Error; response.ErrorMessage = message; }
                        else response.Data = new { success = true, message };
                    }
                    break;
                }

                case "SetUserActive":
                {
                    var targetUserId = parameters.ContainsKey("userId") ? parameters["userId"]?.ToString() : null;
                    var isActive = parameters.ContainsKey("isActive") && Convert.ToBoolean(parameters["isActive"]);
                    if (string.IsNullOrWhiteSpace(targetUserId))
                    {
                        response.Status = MessageStatus.Error;
                        response.ErrorMessage = "userId is required";
                    }
                    else if (targetUserId == uuid && !isActive)
                    {
                        response.Status = MessageStatus.Error;
                        response.ErrorMessage = "You cannot deactivate your own account.";
                    }
                    else
                    {
                        var (ok, message) = _authService.SetUserActiveAsync(targetUserId, isActive).GetAwaiter().GetResult();
                        if (!ok) { response.Status = MessageStatus.Error; response.ErrorMessage = message; }
                        else response.Data = new { success = true, message };
                    }
                    break;
                }

                default:
                    response.Status = MessageStatus.Error;
                    response.ErrorMessage = $"Unknown command: {cmdMessage.Command}";
                    break;
            }
        }
        catch (Exception ex)
        {
            response.Status = MessageStatus.Error;
            response.ErrorMessage = ex.Message;
        }

        using var _ = SendMessageAsync(connectionInfo, response, socket);
    }

    private object TestConnection(JObject connObj)
    {
        var type = (connObj["type"]?.ToString() ?? connObj["Type"]?.ToString() ?? "").ToLower();
        var host = connObj["host"]?.ToString() ?? connObj["Host"]?.ToString();
        var database = connObj["database"]?.ToString() ?? connObj["DatabaseName"]?.ToString();
        var user = connObj["username"]?.ToString() ?? connObj["Username"]?.ToString();
        var pass = connObj["password"]?.ToString() ?? connObj["Password"]?.ToString();
        var connectionString = connObj["connectionString"]?.ToString() ?? connObj["ConnectionString"]?.ToString();
        int.TryParse(connObj["port"]?.ToString() ?? connObj["Port"]?.ToString(), out int port);
        connectionString = connectionString == "" ? null : connectionString;
        try
        {
            System.Data.IDbConnection conn;
            var cs = connectionString ?? type switch
            {
                "mssql"       => $"Server={host},{port};Database={database};User Id={user};Password={pass};TrustServerCertificate=True;Connect Timeout=5;",
                "postgresql"  => $"Host={host};Port={port};Database={database};Username={user};Password={pass};Timeout=5;",
                "mysql"       => $"Server={host};Port={port};Database={database};Uid={user};Pwd={pass};Connect Timeout=5;",
                "oracle"      => $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))(CONNECT_DATA=(SERVICE_NAME={database})));User Id={user};Password={pass};",
                _             => throw new InvalidOperationException($"Unsupported database type: {type}")
            };

            try
            {
                conn = type switch
                {
                    "mssql"      => new Microsoft.Data.SqlClient.SqlConnection(cs),
                    "postgresql" => new Npgsql.NpgsqlConnection(cs),
                    "mysql"      => new MySqlConnector.MySqlConnection(cs),
                    "oracle"     => CreateOracleTestConnection(cs),
                    _            => throw new InvalidOperationException($"Unsupported database type: {type}")
                };
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Oracle"))
            {
                return new { success = false, message = ex.Message };
            }

            using (conn)
            {
                conn.Open();
                conn.Close();
            }

            return new { success = true, message = "Connection successful" };
        }
        catch (Exception ex)
        {
            return new { success = false, message = ex.Message };
        }
    }

    private static Dictionary<string, object> RowToDictionary(dynamic row)
    {
        if (row is IDictionary<string, object> dapperRow)
            return new Dictionary<string, object>(dapperRow);

        if (row is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Object } jsonObj)
        {
            var d = new Dictionary<string, object>();
            foreach (var prop in jsonObj.EnumerateObject())
                d[prop.Name] = JsonElementToClr(prop.Value);
            return d;
        }

        // Last-resort: round-trip through Newtonsoft to get a plain dict
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(row);
        return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json)
               ?? new Dictionary<string, object>();
    }

    private static object JsonElementToClr(System.Text.Json.JsonElement el) => el.ValueKind switch
    {
        System.Text.Json.JsonValueKind.String  => el.GetString(),
        System.Text.Json.JsonValueKind.True    => (object)true,
        System.Text.Json.JsonValueKind.False   => false,
        System.Text.Json.JsonValueKind.Null    => null,
        System.Text.Json.JsonValueKind.Number  => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
        _                                      => el.ToString()
    };

    private static System.Data.IDbConnection CreateOracleTestConnection(string cs)
    {
        try
        {
            var asm = System.Reflection.Assembly.Load("Oracle.ManagedDataAccess");
            var type = asm.GetType("Oracle.ManagedDataAccess.Client.OracleConnection", throwOnError: true)!;
            return (System.Data.IDbConnection)Activator.CreateInstance(type, cs)!;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Oracle client not available: " + ex.Message +
                ". Ensure Oracle.ManagedDataAccess.Core NuGet package is deployed.", ex);
        }
    }

    private void ErrorOccurred(object sender, Exception e)
    {
        Console.WriteLine($"WebSocket client error: {e.Message}");
    }

    private void AuthenticationMessage(object sender, MessageReceivedEventArgs e)
    {
        IWebsocketConnection socket = e.WebSocket;
        ConnectionInfo connectionInfo = _connections.FirstOrDefault(s => s.Value.WebSocket == socket).Value;

        var authMsg = e.Message as AuthenticationMessage;
        string jwt = null;
        string displayName = null;
        bool isFreshLogin = false; // true only for an actual username/password auth, not a token reconnect

        if (authMsg != null)
        {
            if (!string.IsNullOrEmpty(authMsg.Token))
            {
                var userId = _authService.GetUserIdFromToken(authMsg.Token);
                if (!string.IsNullOrEmpty(userId))
                {
                    connectionInfo.UserId = userId;
                    jwt = authMsg.Token;
                }
            }
            else if (!string.IsNullOrEmpty(authMsg.Username))
            {
                jwt = _authService.AuthenticateAsync(new LoginRequest { Username = authMsg.Username, Password = authMsg.Password })
                    .GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(jwt))
                {
                    connectionInfo.UserId = _authService.GetUserIdFromToken(jwt);
                    isFreshLogin = true;
                }
            }

            if (!string.IsNullOrEmpty(connectionInfo.UserId))
            {
                // A token from a stale browser session could belong to a user who was
                // deactivated since it was issued -- re-check IsActive/Roles from the DB
                // on every (re)connect rather than trusting whatever the JWT claims.
                var currentUser = _authService.GetUserAsync(connectionInfo.UserId).GetAwaiter().GetResult();
                if (currentUser == null || !currentUser.IsActive)
                {
                    connectionInfo.UserId = null;
                    jwt = null;
                }
                else
                {
                    connectionInfo.Roles = currentUser.Roles;
                    connectionInfo.Username = currentUser.Username;
                    displayName = string.IsNullOrWhiteSpace(currentUser.FullName) ? currentUser.Username : currentUser.FullName;
                    RegisterUserDatabaseConnections(connectionInfo.UserId);
                    if (isFreshLogin) AuditLogger.Log(connectionInfo.UserId, currentUser.Username, "login");
                }
            }
        }

        dynamic data = new JObject();
        data.Uuid = connectionInfo.ConnectionId;
        data.Menu = new JObject();
        data.Menu.Header = "";
        data.Functions = new JArray(connectionInfo.interpreter.GetFunctions());
        if (jwt != null)
        {
            data.Token = jwt;
            data.Roles = connectionInfo.Roles;
            data.DisplayName = displayName;
        }

        var response = new ResponseMessage
        {
            Status = jwt != null ? MessageStatus.Success : MessageStatus.Error,
            ErrorMessage = jwt != null ? "" : "Authentication failed",
            Data = data
        };

        using var _ = SendMessageAsync(connectionInfo, response, socket);
    }

    public async Task ProcessIncomingMessageAsync(IWebsocketConnection socket, string messageJson)
    {
        ConnectionInfo connectionInfo = _connections.FirstOrDefault(s => s.Value.WebSocket == socket).Value;
        if (connectionInfo == null) return;

        try
        {
            connectionInfo.WebSocketMessageClient.ReceiveMsg(messageJson);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing incoming message: {ex.Message}");
            var errorMessage = new ErrorMessage
            {
                ErrorCode = "500",
                ErrorDescription = ex.Message,
                ErrorDetails = ex.StackTrace
            };
            await SendMessageAsync(connectionInfo, errorMessage, socket);
        }
    }

    private async Task SendMessageAsync(ConnectionInfo connectionInfo, BaseMessage message, IWebsocketConnection socket)
    {
        string msg = JsonConvert.SerializeObject(message);
        await socket.Send(msg);
    }

    public int GetConnectionCount() => _connections.Count;

    public IEnumerable<string> GetConnectedUserIds() =>
        _connections.Values.Where(c => !string.IsNullOrEmpty(c.UserId)).Select(c => c.UserId).Distinct();
}
