using KeyLoad.Query;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlFetchCountClusterProtocol
{
    internal const string Collection = "fetch_count";
    internal const string Label = "label";
    internal const string LabelPath = "/label";
    internal const string Profile = "SQL.FetchCount";
    internal const string UnorderedOne = "SELECT label AS label FROM fetch_count FETCH FIRST ROW ONLY";
    internal const string FirstOne = "SELECT label AS label FROM fetch_count ORDER BY label ASC FETCH FIRST ROW ONLY";
    internal const string NextOne = "SELECT label AS label FROM fetch_count ORDER BY label ASC FETCH NEXT ROWS ONLY";
    internal const string FirstTwo = "SELECT label AS label FROM fetch_count ORDER BY label ASC FETCH FIRST 2 ROWS ONLY";
    internal const string NextThree = "SELECT label AS label FROM fetch_count ORDER BY label ASC FETCH NEXT 3 ROWS ONLY";
    internal const string SingularTwo = "SELECT label AS label FROM fetch_count ORDER BY label ASC FETCH FIRST 2 ROW ONLY";
    internal const string BadSql = "SELECT label AS label FROM fetch_count ORDER BY label ASC FETCH FIRST 0 ROWS ONLY";
    internal const string TiesSql = "SELECT label AS label FROM fetch_count ORDER BY label ASC FETCH FIRST 2 ROWS WITH TIES";
    internal const string ValidationSqlState = "22023";
    internal const string UnsupportedSqlState = "0A000";
    internal const int RowCount = 3;
    internal const int ColdCuts = 2;
    internal const long SeedRevision = 1;
    internal const long DenialAdvance = 1;
    internal const long RepairAdvance = 2;
    internal const string WriterMarker = "fetch-count-writer-";
    internal const string ReaderMarker = "fetch-count-reader-";
    internal static readonly (string Sql, int Count)[] Cases =
        [(UnorderedOne, 1), (FirstOne, 1), (NextOne, 1), (FirstTwo, 2), (NextThree, 3), (SingularTwo, 2)];
    internal static SqlOperationRequest Operation(string sql)
        => new(PostgresClusterState.Partition, sql, AllowFullScan: true);
    internal static AstQueryRequest Ast(int count)
        => new(PostgresClusterState.Partition, new(Collection, null, [new(LabelPath, Label)], null,
            [new(LabelPath, false)], count), AllowFullScan: true);
}
