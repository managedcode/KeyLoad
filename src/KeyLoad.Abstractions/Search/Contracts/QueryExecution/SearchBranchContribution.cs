namespace KeyLoad;

/// <summary>Explains one actual authorized positive-weight branch contribution.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(SearchExplanationAliases.Contribution)]
public sealed record SearchBranchContribution(
    [property: Orleans.Id(0)] SearchBranchKind Branch,
    [property: Orleans.Id(1)] int NativeRank,
    [property: Orleans.Id(2)] double Weight,
    [property: Orleans.Id(3)] double Contribution);
