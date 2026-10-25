using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class CapabilityOutputClusterProtocol
{
    internal const string Collection = "manifest_output";
    internal const int RequestBytes = 32768;
    internal const int LowerBytes = 4096;
    internal const int LowerMode = 1;
    internal static int Expected(int mode) => Options(mode).MaximumResultBytes!.Value;
    internal static QueryExecutionOptions Options(int mode) => mode == LowerMode
        ? new() { MaximumResultBytes = LowerBytes }
        : throw new ArgumentOutOfRangeException(nameof(mode));
    internal static QueryRequest Sql(bool compact, int version = 1)
        => new(PostgresClusterState.Partition, "SELECT TOP (2) " + (compact ? "id AS [key]" : "payload AS payload")
            + " FROM " + Collection + " ORDER BY id ASC", AllowFullScan: true, QueryDialectVersion: version);
    internal static AstQueryRequest Ast(bool compact, int version = 1)
        => new(PostgresClusterState.Partition, new(Collection, null,
            [new(compact ? "/@id" : "/payload", compact ? "key" : "payload")], null, [new("/@id", false)], 2),
            AllowFullScan: true, AstVersion: version);
    internal static string Json(string id) => "{\"id\":\"" + id + "\",\"payload\":\"" + new string('x', 5000) + "\"}";
}
