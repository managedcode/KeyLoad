using KeyLoad.Query;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlNullOrderingClusterProtocol
{
    internal const string Collection = "null_ordering";
    internal const string Label = "label";
    internal const string Profile = "Q1.NullOrdering.v1";
    internal const string ScorePath = "/score";
    internal const string LabelPath = "/label";
    internal const string AscFirst = "SELECT label AS label FROM null_ordering ORDER BY score ASC NULLS FIRST LIMIT 5";
    internal const string AscLast = "SELECT label AS label FROM null_ordering ORDER BY score ASC NULLS LAST LIMIT 5";
    internal const string DescFirst = "SELECT label AS label FROM null_ordering ORDER BY score DESC NULLS FIRST LIMIT 5";
    internal const string DescLast = "SELECT label AS label FROM null_ordering ORDER BY score DESC NULLS LAST LIMIT 5";
    internal const string BadSql = "SELECT label AS label FROM null_ordering ORDER BY score ASC NULLS MIDDLE LIMIT 5";
    internal const string ValidationSqlState = "22023";
    internal const int RowCount = 5;
    internal const int ColdCuts = 2;
    internal const long SeedRevision = 1;
    internal const long DenialAdvance = 1;
    internal const long RepairAdvance = 2;
    internal const string WriterMarker = "null-order-writer-";
    internal const string ReaderMarker = "null-order-reader-";
    internal static string Sql(bool descending, bool first)
        => descending ? first ? DescFirst : DescLast : first ? AscFirst : AscLast;
    internal static SqlOperationRequest Operation(string sql)
        => new(PostgresClusterState.Partition, sql, AllowFullScan: true);
    internal static AstQueryRequest Ast(bool descending, bool first)
        => new(PostgresClusterState.Partition, new(Collection, null, [new(LabelPath, Label)], null,
            [new(ScorePath, descending, first)], RowCount), AllowFullScan: true);
    internal const string UsingProfile = "SQL.OrderUsing.v1";
    internal const string DirectionName = "direction";
    internal const string DirectionOperand = "@direction";
    internal const string DirectionValue = ">";
    internal const string Ascending = " ASC ";
    internal const string Descending = " DESC ";
    internal const string UsingAscending = " USING < ";
    internal const string UsingDescending = " USING > ";
    internal static string Using(bool descending, bool first)
        => Sql(descending, first).Replace(descending ? Descending : Ascending,
            descending ? UsingDescending : UsingAscending, StringComparison.Ordinal);
}
