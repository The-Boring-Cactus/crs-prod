using Newtonsoft.Json.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Server.Core;

// Builds a single joined SQL query from a saved DataModel's tables/relationships plus
// a field/filter/group-by request coming from a dashboard widget. This is the piece
// that lets a widget say "give me customer name and total order amount" instead of
// writing the join itself every time -- the model's relationships are defined once
// and reused. All tables in a model must live on the same DB connection (no
// federated cross-connection joins in this version).
//
// Security note: RunDataModelQuery can be invoked by any authenticated session
// viewing a dashboard (including "viewer" role), not just the model's owner/editor,
// so every table/column/aggregate/operator coming from the request is validated
// against an allowlist before being placed into the generated SQL string. Filter
// *values* are always sent as query parameters, never concatenated.
public static class DataModelQueryBuilder
{
    private static readonly HashSet<string> AllowedAggregates = new(StringComparer.OrdinalIgnoreCase) { "sum", "avg", "count", "min", "max" };
    private static readonly HashSet<string> AllowedOperators = new(StringComparer.OrdinalIgnoreCase) { "=", "!=", "<>", "<", ">", "<=", ">=", "like" };
    private static readonly Regex SafeIdentifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$");

    public class TableDef
    {
        public string Alias { get; set; }
        public string TableName { get; set; }
    }

    public class RelationshipDef
    {
        public string FromTable { get; set; }
        public string FromColumn { get; set; }
        public string ToTable { get; set; }
        public string ToColumn { get; set; }
        public string JoinType { get; set; } = "inner"; // inner | left | right
    }

    public class ModelDef
    {
        public string ConnectionId { get; set; }
        public List<TableDef> Tables { get; set; } = new();
        public List<RelationshipDef> Relationships { get; set; } = new();
    }

    public class FieldSpec
    {
        public string Table { get; set; }
        public string Column { get; set; }
        public string Aggregate { get; set; }
        public string Alias { get; set; }
    }

    public class FilterSpec
    {
        public string Table { get; set; }
        public string Column { get; set; }
        public string Op { get; set; } = "=";
        public object Value { get; set; }
    }

    public class SortSpec
    {
        public string Table { get; set; }
        public string Column { get; set; }
        public string Aggregate { get; set; }
        public string Direction { get; set; } = "asc";
    }

    // Rows become one output row per distinct combination of Rows' values; Column's
    // distinct values become the output columns; each cell is Aggregate(Value) for that
    // (row, column) pair. Reshaping happens in C# after a normal grouped query runs (see
    // ExpandPivot/ReshapeForPivot) rather than via DB-specific PIVOT syntax, so it works
    // the same way across every supported database.
    public class PivotSpec
    {
        public List<FieldSpec> Rows { get; set; } = new();
        public FieldSpec Column { get; set; }
        public FieldSpec Value { get; set; }
    }

    public class QueryRequest
    {
        public List<FieldSpec> Fields { get; set; } = new();
        public List<FilterSpec> Filters { get; set; } = new();
        public List<FieldSpec> GroupBy { get; set; } = new();
        public List<SortSpec> OrderBy { get; set; } = new();
        public int Limit { get; set; } = 1000;
        public PivotSpec Pivot { get; set; }
    }

    public static ModelDef ParseModel(string configJson)
    {
        var obj = JObject.Parse(configJson ?? "{}");
        var model = new ModelDef { ConnectionId = obj["connectionId"]?.ToString() };

        foreach (var t in obj["tables"] as JArray ?? new JArray())
            model.Tables.Add(new TableDef { Alias = t["alias"]?.ToString(), TableName = t["tableName"]?.ToString() });

        foreach (var r in obj["relationships"] as JArray ?? new JArray())
            model.Relationships.Add(new RelationshipDef
            {
                FromTable = r["fromTable"]?.ToString(),
                FromColumn = r["fromColumn"]?.ToString(),
                ToTable = r["toTable"]?.ToString(),
                ToColumn = r["toColumn"]?.ToString(),
                JoinType = r["joinType"]?.ToString() ?? "inner"
            });

        return model;
    }

    public static QueryRequest ParseRequest(JObject parameters)
    {
        var request = new QueryRequest();

        foreach (var f in parameters["fields"] as JArray ?? new JArray())
            request.Fields.Add(new FieldSpec
            {
                Table = f["table"]?.ToString(),
                Column = f["column"]?.ToString(),
                Aggregate = f["aggregate"]?.ToString(),
                Alias = f["alias"]?.ToString()
            });

        foreach (var f in parameters["filters"] as JArray ?? new JArray())
            request.Filters.Add(new FilterSpec
            {
                Table = f["table"]?.ToString(),
                Column = f["column"]?.ToString(),
                Op = f["op"]?.ToString() ?? "=",
                Value = (f["value"] as JValue)?.Value
            });

        foreach (var g in parameters["groupBy"] as JArray ?? new JArray())
            request.GroupBy.Add(new FieldSpec { Table = g["table"]?.ToString(), Column = g["column"]?.ToString() });

        foreach (var o in parameters["orderBy"] as JArray ?? new JArray())
            request.OrderBy.Add(new SortSpec
            {
                Table = o["table"]?.ToString(),
                Column = o["column"]?.ToString(),
                Aggregate = o["aggregate"]?.ToString(),
                Direction = o["direction"]?.ToString() ?? "asc"
            });

        if (parameters["limit"] != null && int.TryParse(parameters["limit"].ToString(), out var limit))
            request.Limit = limit;

        if (parameters["pivot"] is JObject pivotObj)
        {
            var pivot = new PivotSpec();
            foreach (var r in pivotObj["rows"] as JArray ?? new JArray())
                pivot.Rows.Add(new FieldSpec { Table = r["table"]?.ToString(), Column = r["column"]?.ToString() });

            if (pivotObj["column"] is JObject colObj)
                pivot.Column = new FieldSpec { Table = colObj["table"]?.ToString(), Column = colObj["column"]?.ToString() };

            if (pivotObj["value"] is JObject valObj)
                pivot.Value = new FieldSpec
                {
                    Table = valObj["table"]?.ToString(),
                    Column = valObj["column"]?.ToString(),
                    Aggregate = valObj["aggregate"]?.ToString() ?? "sum"
                };

            request.Pivot = pivot;
        }

        return request;
    }

    // Row/column keys used internally for a pivot's underlying flat query -- fixed names
    // (rather than the field's own alias) so ReshapeForPivot can find them unambiguously
    // regardless of what tables/columns the caller picked.
    private const string PivotColumnKey = "__pivotcol";
    private const string PivotValueKey = "__pivotval";
    private static string PivotRowKey(int index) => $"__row{index}";

    // A pivot request is just a grouped query in disguise: row fields + the pivot column
    // become GROUP BY, and the aggregated value becomes the one aggregated SELECT field.
    // The actual row/column reshaping happens afterwards in ReshapeForPivot, once we have
    // real data back from the database.
    private static QueryRequest ExpandPivot(QueryRequest request)
    {
        var pivot = request.Pivot;
        if (pivot.Column == null || pivot.Value == null)
            throw new ArgumentException("A pivot requires both a column field and a value field");

        var fields = new List<FieldSpec>();
        var groupBy = new List<FieldSpec>();
        for (int i = 0; i < pivot.Rows.Count; i++)
        {
            var r = pivot.Rows[i];
            fields.Add(new FieldSpec { Table = r.Table, Column = r.Column, Alias = PivotRowKey(i) });
            groupBy.Add(new FieldSpec { Table = r.Table, Column = r.Column });
        }

        fields.Add(new FieldSpec { Table = pivot.Column.Table, Column = pivot.Column.Column, Alias = PivotColumnKey });
        groupBy.Add(new FieldSpec { Table = pivot.Column.Table, Column = pivot.Column.Column });

        fields.Add(new FieldSpec
        {
            Table = pivot.Value.Table,
            Column = pivot.Value.Column,
            Aggregate = string.IsNullOrEmpty(pivot.Value.Aggregate) ? "sum" : pivot.Value.Aggregate,
            Alias = PivotValueKey
        });

        return new QueryRequest { Fields = fields, Filters = request.Filters, GroupBy = groupBy, OrderBy = request.OrderBy, Limit = request.Limit };
    }

    // Reshapes the flat (row, pivotColumnValue, aggregatedValue) rows produced by a query
    // built from ExpandPivot(request) into one row per distinct row-key combination, with
    // one column per distinct value seen in the pivot column. Column order follows first
    // appearance in the result set; a (row, column) pair with no matching data is left out
    // of that row's dictionary (renders as blank, not zero).
    public static (List<Dictionary<string, object>> rows, List<string> columns) ReshapeForPivot(
        IEnumerable<IDictionary<string, object>> flatRows, PivotSpec pivot)
    {
        var rowLabels = pivot.Rows.Select(r => !string.IsNullOrEmpty(r.Alias) ? r.Alias : $"{r.Table}_{r.Column}").ToList();
        var pivotColumns = new List<string>();
        var outputRows = new List<Dictionary<string, object>>();
        var rowIndexByKey = new Dictionary<string, int>();

        foreach (var flat in flatRows)
        {
            var key = string.Join("", Enumerable.Range(0, rowLabels.Count)
                .Select(i => flat.TryGetValue(PivotRowKey(i), out var v) ? v?.ToString() ?? "" : ""));

            if (!rowIndexByKey.TryGetValue(key, out var idx))
            {
                var outRow = new Dictionary<string, object>();
                for (int i = 0; i < rowLabels.Count; i++)
                    outRow[rowLabels[i]] = flat.TryGetValue(PivotRowKey(i), out var v) ? v : null;
                idx = outputRows.Count;
                outputRows.Add(outRow);
                rowIndexByKey[key] = idx;
            }

            var pivotColumnValue = flat.TryGetValue(PivotColumnKey, out var pcv) ? (pcv?.ToString() ?? "") : "";
            if (!pivotColumns.Contains(pivotColumnValue)) pivotColumns.Add(pivotColumnValue);

            outputRows[idx][pivotColumnValue] = flat.TryGetValue(PivotValueKey, out var val) ? val : null;
        }

        return (outputRows, rowLabels.Concat(pivotColumns).ToList());
    }

    public static (string sql, Dapper.DynamicParameters parameters) Build(ModelDef model, QueryRequest request)
    {
        if (request.Pivot != null)
            request = ExpandPivot(request);

        var tableLookup = model.Tables.ToDictionary(t => t.Alias, t => t.TableName, StringComparer.OrdinalIgnoreCase);

        void ValidateTable(string table)
        {
            if (string.IsNullOrEmpty(table) || !tableLookup.ContainsKey(table))
                throw new ArgumentException($"Unknown table '{table}' in this data model");
        }

        void ValidateColumn(string column)
        {
            if (string.IsNullOrEmpty(column) || !SafeIdentifier.IsMatch(column))
                throw new ArgumentException($"Invalid column name '{column}'");
        }

        var touchedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in request.Fields)
        {
            ValidateTable(f.Table); ValidateColumn(f.Column);
            if (!string.IsNullOrEmpty(f.Aggregate) && !AllowedAggregates.Contains(f.Aggregate))
                throw new ArgumentException($"Invalid aggregate '{f.Aggregate}'");
            touchedTables.Add(f.Table);
        }
        foreach (var f in request.Filters)
        {
            ValidateTable(f.Table); ValidateColumn(f.Column);
            if (!AllowedOperators.Contains(f.Op))
                throw new ArgumentException($"Invalid filter operator '{f.Op}'");
            touchedTables.Add(f.Table);
        }
        foreach (var g in request.GroupBy) { ValidateTable(g.Table); ValidateColumn(g.Column); touchedTables.Add(g.Table); }
        foreach (var o in request.OrderBy)
        {
            ValidateTable(o.Table); ValidateColumn(o.Column);
            if (!string.IsNullOrEmpty(o.Aggregate) && !AllowedAggregates.Contains(o.Aggregate))
                throw new ArgumentException($"Invalid aggregate '{o.Aggregate}'");
            touchedTables.Add(o.Table);
        }

        if (request.Fields.Count == 0)
            throw new ArgumentException("At least one field is required");

        var joinPlan = PlanJoins(model, touchedTables);

        var sql = new StringBuilder();
        var parameters = new Dapper.DynamicParameters();

        string Col(string table, string column) => $"{table}.{column}";
        string Aggregated(string aggregate, string expr) => string.IsNullOrEmpty(aggregate) ? expr : $"{aggregate.ToUpper()}({expr})";

        // SELECT
        var selectParts = request.Fields.Select(f =>
        {
            var expr = Aggregated(f.Aggregate, Col(f.Table, f.Column));
            var alias = !string.IsNullOrEmpty(f.Alias) && SafeIdentifier.IsMatch(f.Alias) ? f.Alias : $"{f.Table}_{f.Column}{(string.IsNullOrEmpty(f.Aggregate) ? "" : "_" + f.Aggregate.ToLower())}";
            return $"{expr} AS {alias}";
        });
        sql.Append("SELECT ").Append(string.Join(", ", selectParts));

        // FROM + JOINs
        var baseTableName = tableLookup[joinPlan.Root];
        sql.Append($" FROM {baseTableName} {joinPlan.Root}");
        foreach (var (toAlias, rel, reversed) in joinPlan.Edges)
        {
            var joinKeyword = (reversed ? InvertJoin(rel.JoinType) : rel.JoinType).ToUpper() switch
            {
                "LEFT" => "LEFT JOIN",
                "RIGHT" => "RIGHT JOIN",
                _ => "INNER JOIN"
            };
            var toTableName = tableLookup[toAlias];
            sql.Append($" {joinKeyword} {toTableName} {toAlias} ON {rel.FromTable}.{rel.FromColumn} = {rel.ToTable}.{rel.ToColumn}");
        }

        // WHERE
        if (request.Filters.Count > 0)
        {
            var whereParts = new List<string>();
            for (int i = 0; i < request.Filters.Count; i++)
            {
                var f = request.Filters[i];
                var paramName = $"p{i}";
                var op = f.Op == "!=" ? "<>" : f.Op;
                whereParts.Add($"{Col(f.Table, f.Column)} {op.ToUpper()} @{paramName}");
                parameters.Add(paramName, f.Value);
            }
            sql.Append(" WHERE ").Append(string.Join(" AND ", whereParts));
        }

        // GROUP BY: explicit list, or implicit (any non-aggregated field) when some fields are aggregated
        var groupByFields = request.GroupBy.Count > 0
            ? request.GroupBy
            : (request.Fields.Any(f => !string.IsNullOrEmpty(f.Aggregate)) ? request.Fields.Where(f => string.IsNullOrEmpty(f.Aggregate)).ToList() : new List<FieldSpec>());
        if (groupByFields.Count > 0)
            sql.Append(" GROUP BY ").Append(string.Join(", ", groupByFields.Select(g => Col(g.Table, g.Column))));

        // ORDER BY
        if (request.OrderBy.Count > 0)
        {
            var orderParts = request.OrderBy.Select(o =>
            {
                var dir = string.Equals(o.Direction, "desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
                return $"{Aggregated(o.Aggregate, Col(o.Table, o.Column))} {dir}";
            });
            sql.Append(" ORDER BY ").Append(string.Join(", ", orderParts));
        }

        // LIMIT (hard cap to prevent runaway result sets)
        var limit = Math.Clamp(request.Limit, 1, 10000);
        sql.Append($" LIMIT {limit}");

        return (sql.ToString(), parameters);
    }

    private static string InvertJoin(string joinType) => joinType.ToLower() switch
    {
        "left" => "right",
        "right" => "left",
        _ => joinType
    };

    private class JoinPlan
    {
        public string Root { get; set; }
        public List<(string toAlias, RelationshipDef rel, bool reversed)> Edges { get; } = new();
    }

    // BFS over the model's relationship graph to find a set of joins connecting all
    // touched tables, starting from one of them. May pass through untouched "bridge"
    // tables if that's the only way two touched tables are related.
    private static JoinPlan PlanJoins(ModelDef model, HashSet<string> touchedTables)
    {
        var root = touchedTables.First();
        var plan = new JoinPlan { Root = root };

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var remaining = new HashSet<string>(touchedTables, StringComparer.OrdinalIgnoreCase);
        remaining.Remove(root);

        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0 && remaining.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var rel in model.Relationships)
            {
                string next = null; bool reversed = false;
                if (string.Equals(rel.FromTable, current, StringComparison.OrdinalIgnoreCase) && !visited.Contains(rel.ToTable))
                    (next, reversed) = (rel.ToTable, false);
                else if (string.Equals(rel.ToTable, current, StringComparison.OrdinalIgnoreCase) && !visited.Contains(rel.FromTable))
                    (next, reversed) = (rel.FromTable, true);

                if (next != null)
                {
                    visited.Add(next);
                    plan.Edges.Add((next, rel, reversed));
                    remaining.Remove(next);
                    queue.Enqueue(next);
                }
            }
        }

        if (remaining.Count > 0)
            throw new ArgumentException($"No relationship in this data model connects table(s): {string.Join(", ", remaining)}");

        return plan;
    }
}
