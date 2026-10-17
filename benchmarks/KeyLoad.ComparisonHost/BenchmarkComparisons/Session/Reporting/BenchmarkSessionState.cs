using System.Collections.Immutable;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

internal sealed record BenchmarkSessionState(string SchemaVersion, string SessionId, string Target, int NodeCount,
    string SourceRevision, long RunId, int Attempt, long JobId, string Repository, string Ref, string Workflow,
    BenchmarkSessionImages Images, TargetProfile? TargetProfile, BenchmarkSessionRuntime Runtime,
    ImmutableArray<BenchmarkSessionAssembly> Assemblies,
    ImmutableArray<BenchmarkSessionArtifact> Cases, string Outcome, string? Reason);
