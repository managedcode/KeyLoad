using KeyLoad.Query;
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlNullOrderingProtocol
{
    internal const string Collection = "null_ordering";
    internal const string Root = "root";
    internal const string Reader = "null_order_reader";
    internal const string Score = "/score";
    internal const string Label = "/label";
    internal const string LabelName = "label";
    internal const string Profile = "Q1.NullOrdering.v1";
    internal const int ResultCount = 5;
    internal const int ColdCuts = 2;
    internal const long InitialEpoch = 1;
    internal const long DeniedEpoch = 2;
    internal const long RepairedEpoch = 3;
    internal const string Invalid = "The original ordering result is missing.";
    internal static QueryRequest Sql(PartitionRef partition, bool descending, bool first, int limit = ResultCount)
        => new(partition, "SELECT label AS label FROM null_ordering ORDER BY score "
            + (descending ? "DESC " : "ASC ") + (first ? "NULLS FIRST" : "NULLS LAST")
            + " LIMIT " + limit.ToString(global::System.Globalization.CultureInfo.InvariantCulture), AllowFullScan: true);
    internal static AstQueryRequest Ast(PartitionRef partition, bool descending, bool first, int limit = ResultCount)
        => new(partition, new(Collection, null, [new(Label, LabelName)], null,
            [new(Score, descending, first)], limit), AllowFullScan: true);
    internal const string UsingProfile = "SQL.OrderUsing.v1";
    internal const string Ascending = " ASC ";
    internal const string Descending = " DESC ";
    internal const string UsingAscending = " USING < ";
    internal const string UsingDescending = " USING > ";
    internal static QueryRequest Using(PartitionRef partition, bool descending, bool first, int limit = ResultCount)
    {
        var original = Sql(partition, descending, first, limit);
        return original with
        {
            Sql = original.Sql.Replace(descending ? Descending : Ascending,
            descending ? UsingDescending : UsingAscending, StringComparison.Ordinal)
        };
    }
}
