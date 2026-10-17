using System.Globalization;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedKurrentSettings
{
    private const string ClusterSize = "KURRENTDB_CLUSTER_SIZE";
    private const string NodeIp = "KURRENTDB_NODE_IP";
    private const string NodePort = "KURRENTDB_NODE_PORT";
    private const string ReplicationIp = "KURRENTDB_REPLICATION_IP";
    private const string ReplicationPort = "KURRENTDB_REPLICATION_PORT";
    private const string NodeAdvertise = "KURRENTDB_NODE_HOST_ADVERTISE_AS";
    private const string ReplicationAdvertise = "KURRENTDB_REPLICATION_HOST_ADVERTISE_AS";
    private const string Insecure = "KURRENTDB_INSECURE";
    private const string DiscoverDns = "KURRENTDB_DISCOVER_VIA_DNS";
    private const string GossipSeeds = "KURRENTDB_GOSSIP_SEED";
    private const string Database = "KURRENTDB_DB";
    private const string Data = "/var/lib/kurrentdb";
    private const string BindAll = "0.0.0.0";
    private const string HttpPort = "2113";
    private const string TcpPort = "1112";
    private const string Enabled = "true";
    private const string Disabled = "false";
    private const string PortSeparator = ":";
    private const string NativeHostSuffix = ".dev.internal";

    internal static string NativeHost(string name) => name + NativeHostSuffix;

    internal static void Configure(IResourceBuilder<ContainerResource> node, string name, string[] names)
    {
        const int BoundaryValue = 1;
        const char SeparatorCharacter = ',';

        node.WithEnvironment(ClusterSize, names.Length.ToString(CultureInfo.InvariantCulture))
            .WithEnvironment(NodeIp, BindAll).WithEnvironment(NodePort, HttpPort)
            .WithEnvironment(ReplicationIp, BindAll).WithEnvironment(ReplicationPort, TcpPort)
            .WithEnvironment(NodeAdvertise, NativeHost(name)).WithEnvironment(ReplicationAdvertise, NativeHost(name))
            .WithEnvironment(Insecure, Enabled).WithEnvironment(DiscoverDns, Disabled)
            .WithEnvironment(Database, Data);
        if (names.Length > BoundaryValue)
        {
            node.WithEnvironment(GossipSeeds, string.Join(SeparatorCharacter, names.Where(peer => peer != name)
                .Select(peer => NativeHost(peer) + PortSeparator + HttpPort)));
        }
    }
}
