namespace KeyLoad.Query.Features.Search;

internal readonly record struct PackedAnnApproximateResult(AnnCandidate[] Candidates, AnnSearchMode Mode,
    int ExpansionPasses, long FallbackDistanceEvaluations);
