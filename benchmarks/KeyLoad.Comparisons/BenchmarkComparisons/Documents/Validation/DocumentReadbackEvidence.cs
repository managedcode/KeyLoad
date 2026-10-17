namespace KeyLoad.Comparisons;

/// <summary>Actual fully enumerated cardinality and compared complete-state digests.</summary>
public sealed record DocumentReadbackEvidence(int Records, string ExpectedSha256, string ActualSha256);
