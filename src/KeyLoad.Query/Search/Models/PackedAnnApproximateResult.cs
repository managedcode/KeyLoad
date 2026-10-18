namespace KeyLoad.Query.Features.Search;

internal readonly record struct PackedAnnApproximateResult(AnnCandidate[] Candidates, int ExpansionPasses);
