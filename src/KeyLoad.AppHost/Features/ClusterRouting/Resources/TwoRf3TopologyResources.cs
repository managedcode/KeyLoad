using System.Globalization;

namespace KeyLoad.AppHost.Features.ClusterRouting;

internal static class TwoRf3TopologyResources
{
    private const string PeerPrefix = "KeyLoad__Peers__";

    internal static void ApplyPeerEndpoints(IResourceBuilder<ContainerResource> resource, string[] group)
    {
        const int FirstPeerIndex = 0;
        for (var index = FirstPeerIndex; index < group.Length; index++)
        {
            resource.WithEnvironment(PeerPrefix + index.ToString(CultureInfo.InvariantCulture), TwoRf3ClusterResources.Origin(group[index]));
        }
    }

}
