using System.Collections.Immutable;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed record HybridQualityDocument(string Id, string Json, ImmutableArray<float> Embedding);

internal sealed record HybridQualityJudgment(string Id, int Grade);

internal sealed record HybridQualityQuery(
    string Name,
    string? Text,
    ImmutableArray<float>? Vector,
    ImmutableArray<string> EligibleIds,
    ImmutableArray<int> Grades,
    bool IncludeGraph);

internal sealed record HybridQualityBranch(
    string Name,
    GlobalBranchKind Kind,
    ImmutableArray<RankedDocument> Hits);

internal sealed record HybridQualityMetrics(
    double RecallAt5,
    double RecallAt10,
    double MeanReciprocalRankAt10,
    double NdcgAt5,
    double NdcgAt10);

internal sealed record HybridQualityWindowObservation(
    int Width,
    ImmutableArray<string> CandidateIds,
    double CandidateRecall,
    ImmutableArray<string> FusedIds,
    ImmutableArray<double> FusedScores,
    HybridQualityMetrics Metrics,
    bool AnyTruncated);
