using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PhysicalShardCatalogVoterIds
{
    internal const string First = "http://node1:8080";
    internal const string Second = "http://node2:8080";
    internal const string Third = "http://node3:8080";

    internal static ImmutableArray<string> Standard => [First, Second, Third];
}
