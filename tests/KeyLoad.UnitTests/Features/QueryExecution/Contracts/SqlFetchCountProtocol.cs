using KeyLoad.Query;
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlFetchCountProtocol
{
    internal const string Collection = "fetch_count";
    internal const string Root = "root";
    internal const string Reader = "fetch_count_reader";
    internal const string Label = "/label";
    internal const string LabelName = "label";
    internal const string Profile = "TSQL.OffsetFetch.v1";
    internal const string Prefix = "SELECT label AS label FROM fetch_count ORDER BY label ASC ";
    internal const int ResultCount = 3;
    internal const int ColdCuts = 2;
    internal const long InitialEpoch = 1;
    internal const long DeniedEpoch = 2;
    internal const long RepairedEpoch = 3;
    internal static readonly (string Tail, int Count)[] Cases =
        [("OFFSET 0 ROWS FETCH FIRST 1 ROW ONLY", 1), ("OFFSET 0 ROWS FETCH NEXT 1 ROW ONLY", 1),
         ("OFFSET 0 ROWS FETCH FIRST 2 ROWS ONLY", 2), ("OFFSET 0 ROWS FETCH NEXT 3 ROWS ONLY", 3), ("OFFSET 0 ROWS FETCH FIRST 2 ROW ONLY", 2)];
    internal static QueryRequest Sql(PartitionRef partition, string tail)
        => new(partition, Prefix + tail, AllowFullScan: true);
    internal static AstQueryRequest Ast(PartitionRef partition, int count)
        => new(partition, new(Collection, null, [new(Label, LabelName)], null,
            [new(Label, false)], count), AllowFullScan: true);
}
