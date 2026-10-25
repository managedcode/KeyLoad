using System.Collections.Immutable;
namespace KeyLoad.FunctionalTests.Search;

internal sealed record HybridQualityQuery(
    string Name,
    string? Text,
    ImmutableArray<float>? Vector,
    ImmutableArray<string> EligibleIds,
    ImmutableArray<int> Grades,
    bool IncludeGraph);
