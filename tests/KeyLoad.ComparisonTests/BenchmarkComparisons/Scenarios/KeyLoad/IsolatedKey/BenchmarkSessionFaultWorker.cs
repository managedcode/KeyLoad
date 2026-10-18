namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Fault-checks identity bound to the authentic shared database session.</summary>
internal sealed record BenchmarkSessionFaultWorker(string Target, int NodeCount, string Profile, string SourceRevision,
    long RunId, int Attempt, string Repository, string Ref, string Workflow, long JobId);
