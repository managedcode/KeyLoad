namespace KeyLoad.Query.QueryExecution;

internal enum QueryAccessKind { Point, IndexPrefix, IndexRange, FullScan }

/// <summary>Owns the exact storage access selected once for planning and execution.</summary>
internal sealed record QueryAccessPlan(QueryAccessKind Kind, string AccessPath, byte[] Prefix,
    string? IndexName = null, string[]? IndexFields = null, string? PointId = null,
    byte[]? AfterKey = null, byte[]? UntilKey = null);
