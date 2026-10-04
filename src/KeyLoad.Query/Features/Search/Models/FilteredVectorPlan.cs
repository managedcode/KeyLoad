namespace KeyLoad.Query.Features.Search;

internal readonly record struct FilteredVectorPlan(
    int EligibleCount,
    int ResultCount,
    int Capacity,
    int InitialBreadth,
    bool UseExact);
