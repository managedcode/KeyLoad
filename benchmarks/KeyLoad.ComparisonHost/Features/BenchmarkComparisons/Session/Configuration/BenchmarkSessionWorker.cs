namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Exact authenticated worker provenance retained by the native session.</summary>
internal sealed record BenchmarkSessionWorker(string Target, int NodeCount, string SourceRevision,
    long RunId, int Attempt, string Repository, string Ref, string Workflow, long JobId);
