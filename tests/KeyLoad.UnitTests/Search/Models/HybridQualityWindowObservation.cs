using System.Collections.Immutable;
namespace KeyLoad.UnitTests.Search;

internal sealed record HybridQualityWindowObservation(
    int Width,
    ImmutableArray<string> CandidateIds,
    double CandidateRecall,
    ImmutableArray<string> FusedIds,
    ImmutableArray<double> FusedScores,
    HybridQualityMetrics Metrics,
    bool AnyTruncated);
