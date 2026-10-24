using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class QueryCapabilityVersionExpected
{
    internal static QueryCapabilityManifest Manifest(DatabaseLimits limits) => new(1, 1, "TSQL.ScriptDOM180", "atomicPartition", "decimal", "distinctFromNull",
        ["SQL", "JSON", "C#"], ["comparison", "AND", "OR", "NOT", "IN", "NOT IN", "BETWEEN", "NOT BETWEEN", "IS NULL", "IS NOT NULL", "IS DISTINCT FROM", "IS NOT DISTINCT FROM", "AST.IS MISSING"],
        limits.MaxResults, limits.MaxScanRecords, limits.MaxBatchBytes, limits.MaxQueryDepth, true, true, limits.MaxQueryReadBytes,
        ["Q1", "documentChangeFeed", "scalarLiveQuery", "modelViews", "Q2.InnerJoin", "Q2.InnerJoin.LeftPredicate", "Q2.QueueEntityRefJoin", "Q2.EventEntityRefJoin", "Q1.Offset", "Q2.InnerJoin.Offset", "Q1.NullOrdering", "TSQL.ScriptDOM180", "TSQL.Top", "TSQL.OffsetFetch", "TSQL.NullDistinct", "TSQL.SelectAll", "TSQL.ImplicitProjectionAlias", "TSQL.DefaultInnerJoin", "TSQL.OrdinalOrder", "TSQL.NamedParameters", "TSQL.SessionSlots", "TSQL.DecimalScalar", "TSQL.NationalStringLiteral", "graph-search", "AST.Explain"], [1], [1]);
}
