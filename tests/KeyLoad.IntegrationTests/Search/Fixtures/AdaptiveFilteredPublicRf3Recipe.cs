using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AdaptiveFilteredPublicRf3Recipe
{
    private const string Full = "100-percent";
    private const string Ten = "10-percent";
    private const string One = "1-percent";
    private const string Tenth = "0.1-percent";
    private const string Low = "correlated-low-300";
    private const string High = "correlated-high-300";
    private const string Duplicate = "duplicate-and-unknown";
    private const string Empty = "empty";
    internal static AdaptiveFilteredPublicRf3Cohort[] Cohorts()
        => [Create(Full, Enumerable.Range(0, 1_000), true), Create(Ten, Enumerable.Range(0, 100).Select(i => i * 10)),
            Create(One, Enumerable.Range(0, 10).Select(i => i * 100)), Create(Tenth, [0]),
            Create(Low, Enumerable.Range(0, 300)), Create(High, Enumerable.Range(700, 300)),
            new(Duplicate, [AdaptiveFilteredPublicRf3Protocol.Id(17), AdaptiveFilteredPublicRf3Protocol.Missing,
                AdaptiveFilteredPublicRf3Protocol.Id(17)], [17]), new(Empty, [], [])];

    private static AdaptiveFilteredPublicRf3Cohort Create(string name, IEnumerable<int> values, bool full = false)
    {
        var eligible = values.ToArray();
        return new(name, full ? null : [.. eligible.Select(AdaptiveFilteredPublicRf3Protocol.Id)], eligible);
    }
    internal static SearchRequest Search(AdaptiveFilteredPublicRf3State state, ImmutableArray<string>? allowed, int limit)
        => new(state.Scenario.Partition, NativeAnnMaintenanceRf3Scenario.Collection,
            VectorField: NativeAnnMaintenanceRf3Scenario.Field, Vector: [1, 0], Space: NativeAnnMaintenanceRf3Scenario.Space,
            Limit: limit, FusionConstant: AdaptiveFilteredPublicRf3Protocol.Fusion, AllowedIds: allowed);

    internal static RankedDocument[] Expected(PartitionRef partition, IEnumerable<int> eligible, int limit)
        => eligible.OrderDescending().Take(limit).Select((ordinal, rank) => new RankedDocument(
            new(new(partition, NativeAnnMaintenanceRf3Scenario.Collection, AdaptiveFilteredPublicRf3Protocol.Id(ordinal)),
                AdaptiveFilteredPublicRf3Protocol.Revision, AdaptiveFilteredPublicRf3Protocol.Body, false, []),
            1d / (AdaptiveFilteredPublicRf3Protocol.Fusion + rank + 1))).ToArray();
}
