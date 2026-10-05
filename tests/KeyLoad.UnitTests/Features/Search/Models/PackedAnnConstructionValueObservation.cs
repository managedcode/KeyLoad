namespace KeyLoad.UnitTests.Features.Search;

internal readonly record struct PackedAnnConstructionValueObservation(
    int GreedyNode,
    int CandidateCount,
    long WorkUnits,
    long DistanceEvaluations,
    long EdgeVisits,
    long AllocatedBytes);
