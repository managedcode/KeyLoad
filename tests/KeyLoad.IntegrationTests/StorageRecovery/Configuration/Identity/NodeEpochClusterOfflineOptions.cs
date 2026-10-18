using System.Globalization;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NodeEpochClusterOfflineOptions
{
    internal static NodeOptions Create(string destination, NodeEpochClusterProfile profile, string nodeName)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!ClusterFixtureProtocol.IsNodeName(nodeName))
        {
            throw new ArgumentException("The node must belong to the closed CLUSTER contract.", nameof(nodeName));
        }
        var options = new NodeOptions
        {
            DataDirectory = Path.GetFullPath(destination),
            PhysicalShardId = profile.PhysicalShardId,
            Incarnation = profile.Incarnation,
            ClusterId = "keyload-" + profile.Incarnation.ToString("N", CultureInfo.InvariantCulture),
            SigningKey = profile.SigningKey,
            PeerSecret = profile.PeerSecret,
            AdminKey = profile.AdminKey,
            PublicEndpoint = "http://" + nodeName + ":8080",
            SiloAddress = nodeName,
            Peers = ["http://node1:8080", "http://node2:8080", "http://node3:8080"],
            AllowPrivateNetworkHttp = true,
            SnapshotThreshold = NodeEpochClusterProtocol.SnapshotThreshold
        };
        options.Validate();
        return options;
    }
}
