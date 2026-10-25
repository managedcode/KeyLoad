using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueryCapabilityVersionClusterExpected
{
    private const int UnconfiguredResultBytes = int.MaxValue;

    internal static QueryCapabilityManifest Manifest() => new(1, 1, "TSQL.ScriptDOM180", "atomicPartition", "decimal", "distinctFromNull",
        ["SQL", "JSON", "C#"], ["comparison", "AND", "OR", "NOT", "IN", "NOT IN", "BETWEEN", "NOT BETWEEN", "IS NULL", "IS NOT NULL", "IS DISTINCT FROM", "IS NOT DISTINCT FROM", "AST.IS MISSING"],
        UnconfiguredResultBytes, true, true,
        ["Q1", "documentChangeFeed", "scalarLiveQuery", "modelViews", "Q2.InnerJoin", "Q2.InnerJoin.LeftPredicate", "Q2.QueueEntityRefJoin", "Q2.EventEntityRefJoin", "Q1.Offset", "Q2.InnerJoin.Offset", "Q1.NullOrdering", "TSQL.ScriptDOM180", "TSQL.Top", "TSQL.OffsetFetch", "TSQL.NullDistinct", "TSQL.SelectAll", "TSQL.ImplicitProjectionAlias", "TSQL.DefaultInnerJoin", "TSQL.OrdinalOrder", "TSQL.NamedParameters", "TSQL.SessionSlots", "TSQL.DecimalScalar", "TSQL.NationalStringLiteral", "graph-search", "AST.Explain"], [1], [1]);
}
