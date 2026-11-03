using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueryCapabilityParityExpected
{
    internal static QueryCapabilityManifest Manifest() => new("TSQL.ScriptDOM180", "atomicPartition", "decimal", "distinctFromNull",
        ["SQL", "JSON", "C#"], ["comparison", "AND", "OR", "NOT", "IN", "NOT IN", "BETWEEN", "NOT BETWEEN", "IS NULL", "IS NOT NULL", "IS DISTINCT FROM", "IS NOT DISTINCT FROM", "AST.IS MISSING"], true, true,
        ["scalar", "documentChangeFeed", "scalarLiveQuery", "modelViews", "InnerJoin", "InnerJoin.LeftPredicate", "QueueEntityRefJoin", "EventEntityRefJoin", "Offset", "InnerJoin.Offset", "NullOrdering", "TSQL.ScriptDOM180", "TSQL.Top", "TSQL.OffsetFetch", "TSQL.NullDistinct", "TSQL.SelectAll", "TSQL.ImplicitProjectionAlias", "TSQL.DefaultInnerJoin", "TSQL.OrdinalOrder", "TSQL.NamedParameters", "TSQL.SessionSlots", "TSQL.DecimalScalar", "TSQL.NationalStringLiteral", "graph-search", "AST.Explain"]);
}
