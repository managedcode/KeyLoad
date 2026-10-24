namespace KeyLoad.UnitTests.Search;

internal sealed record HybridQualityMetrics(
    double RecallAt5,
    double RecallAt10,
    double MeanReciprocalRankAt10,
    double NdcgAt5,
    double NdcgAt10);
