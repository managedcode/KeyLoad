using KeyLoad.Query.Search;

namespace KeyLoad.FunctionalTests.Search;

internal readonly record struct PackedAnnRecallObservation(double Recall, int QueryCount,
    HashSet<AnnSearchMode> Modes, long WorkUnits, long Distances, long Edges);
