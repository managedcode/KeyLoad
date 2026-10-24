namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

/// <summary>Retains exact workflow identities without assigning publication provenance to local execution.</summary>
internal sealed record BenchmarkSessionWorker(string Target, int NodeCount, string? SourceRevision,
    long? RunId, int? Attempt, string? Repository, string? Ref, string? Workflow, long? JobId);
