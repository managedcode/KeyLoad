namespace KeyLoad.Comparisons;

/// <summary>Fresh document-only execution setup, with explicit seed versus configure-only intent.</summary>
public sealed record DocumentComparisonInitialization(IComparisonCorpus Corpus, bool Seed, int MaximumIdentityExclusive);
