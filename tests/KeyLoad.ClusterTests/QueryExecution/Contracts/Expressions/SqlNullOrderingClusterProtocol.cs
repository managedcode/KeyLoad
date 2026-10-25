using System.Text.Json;
using KeyLoad.Query;
namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlNullOrderingClusterProtocol
{
    internal const string Collection = "null_ordering";
    internal const string Label = "label";
    internal const string Profile = "Q1.NullOrdering";
    internal const string ScorePath = "/score";
    internal const string LabelPath = "/label";
    internal const string BadSql = "SELECT label AS label FROM null_ordering ORDER BY score ASC NULLS MIDDLE LIMIT 5";
    internal const string ValidationSqlState = "22023";
    internal const int RowCount = 5;
    internal const int ColdCuts = 2;
    internal const long SeedRevision = 1;
    internal const long DenialAdvance = 1;
    internal const long RepairAdvance = 2;
    internal const string WriterMarker = "null-order-writer-";
    internal const string ReaderMarker = "null-order-reader-";
    internal static SqlOperationRequest Operation(string sql)
        => new(PostgresClusterState.Partition, sql, AllowFullScan: true);
    internal static AstQueryRequest Ast(bool descending, bool first)
        => new(PostgresClusterState.Partition, new(Collection, null, [new(LabelPath, Label)], null,
            [new(ScorePath, descending, first)], RowCount), AllowFullScan: true);
    internal const string CurrentSql = "SELECT TOP (5) label AS label FROM null_ordering ORDER BY score ASC";
    internal const string AstDriverSql = "EXEC keyload_query_ast $1";
    private const string AstSql = "EXEC keyload_query_ast @request";
    internal const string CurrentAscending = " ASC";
    internal static SqlOperationRequest AstOperation(bool descending, bool first)
        => new(PostgresClusterState.Partition, AstSql, new(StringComparer.Ordinal)
        {
            [PostgresClusterProtocol.Request] = JsonSerializer.SerializeToElement(Envelope(descending, first), JsonDefaults.Options)
        });
    internal static global::Npgsql.NpgsqlParameter[] AstArguments(bool descending, bool first)
        => [new() { NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Jsonb,
            Value = JsonSerializer.Serialize(Envelope(descending, first), JsonDefaults.Options) }];
    private static Dictionary<string, JsonElement> Envelope(bool descending, bool first)
        => new(StringComparer.Ordinal)
        {
            [PostgresClusterProtocol.Request] = JsonSerializer.SerializeToElement(Ast(descending, first), JsonDefaults.Options)
        };
    internal const string UsingProfile = "SQL.OrderUsing";
    internal const string DirectionName = "direction";
    internal const string DirectionOperand = "@direction";
    internal const string DirectionValue = ">";
}
