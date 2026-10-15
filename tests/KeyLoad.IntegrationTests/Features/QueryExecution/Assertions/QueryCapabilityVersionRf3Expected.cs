using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryCapabilityVersionRf3Expected
{
    internal static QueryCapabilityManifest Manifest(DatabaseLimits limits) => new(1, 1, "TSQL.ScriptDOM180.v1", "atomicPartition", "decimal", "distinctFromNull",
        ["SQL", "JSON", "C#"], ["comparison", "AND", "OR", "NOT", "IN", "NOT IN", "BETWEEN", "NOT BETWEEN", "IS NULL", "IS NOT NULL", "IS DISTINCT FROM", "IS NOT DISTINCT FROM", "AST.IS MISSING"],
        limits.MaxResults, limits.MaxScanRecords, limits.MaxBatchBytes, limits.MaxQueryDepth, true, true, limits.MaxQueryReadBytes,
        ["Q1", "documentChangeFeed", "scalarLiveQuery", "modelViewsV1", "Q2.InnerJoin.v1", "Q2.InnerJoin.LeftPredicate.v1", "Q2.QueueEntityRefJoin.v1", "Q2.EventEntityRefJoin.v1", "Q1.Offset.v1", "Q2.InnerJoin.Offset.v1", "Q1.NullOrdering.v1", "TSQL.ScriptDOM180.v1", "TSQL.Top.v1", "TSQL.OffsetFetch.v1", "TSQL.NullDistinct.v1", "TSQL.SelectAll.v1", "TSQL.ImplicitProjectionAlias.v1", "TSQL.DefaultInnerJoin.v1", "TSQL.OrdinalOrder.v1", "TSQL.NamedParameters.v1", "TSQL.NativeSessionSlots.v1", "TSQL.DecimalScalar.v1", "TSQL.NationalStringLiteral.v1", "graph-search-v1", "AST.Explain.v1"], [1, 2], [1, 2]);
}
