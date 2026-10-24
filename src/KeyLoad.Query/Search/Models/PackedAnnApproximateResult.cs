namespace KeyLoad.Query.Search;

internal readonly record struct PackedAnnApproximateResult(AnnCandidate[] Candidates, int ExpansionPasses);
