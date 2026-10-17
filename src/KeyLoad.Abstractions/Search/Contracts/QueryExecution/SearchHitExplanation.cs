using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Explains a selected hit at its original authorized scoped read cut.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(SearchExplanationAliases.Explanation)]
public sealed record SearchHitExplanation(
    [property: Orleans.Id(0)] int FusionConstant,
    [property: Orleans.Id(1)] ImmutableArray<SearchBranchContribution> Contributions);
