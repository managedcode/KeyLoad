using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Names only canonical authorized hybrid ranking branches.</summary>
public enum SearchBranchKind
{
    /// <summary>The canonical lexical branch.</summary>
    Text,
    /// <summary>The canonical vector similarity branch.</summary>
    Vector,
    /// <summary>The canonical shortest-hop graph retrieval branch.</summary>
    Graph
}

/// <summary>Explains one actual authorized positive-weight branch contribution.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(SearchExplanationAliases.Contribution)]
public sealed record SearchBranchContribution(
    [property: Orleans.Id(0)] SearchBranchKind Branch,
    [property: Orleans.Id(1)] int NativeRank,
    [property: Orleans.Id(2)] double Weight,
    [property: Orleans.Id(3)] double Contribution);

/// <summary>Explains a selected hit at its original authorized scoped read cut.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(SearchExplanationAliases.Explanation)]
public sealed record SearchHitExplanation(
    [property: Orleans.Id(0)] int FusionConstant,
    [property: Orleans.Id(1)] ImmutableArray<SearchBranchContribution> Contributions);

internal static class SearchExplanationAliases
{
    internal const string Contribution = "keyload.search.branch-contribution.v1";
    internal const string Explanation = "keyload.search.hit-explanation.v1";
}
