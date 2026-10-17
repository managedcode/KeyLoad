using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal readonly record struct PackedAnnRecallObservation(double Recall, int QueryCount,
    HashSet<AnnSearchMode> Modes, long WorkUnits, long Distances, long Edges);
