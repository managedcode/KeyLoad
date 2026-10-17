using System.Security.Cryptography;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Same six leased listeners and immutable identities across all native-owner restarts.</summary>
internal sealed class PartitionMovementLateNativeSettings(ControlledPartitionMovementLoopbackListeners listeners, string root)
{
    internal const int GroupSize = 3;
    internal const int OwnerCount = 6;
    private const int FirstVoter = 0;
    private const int SecretBytes = 32;
    private const int NodeOrdinalStep = 1;
    private const string Enabled = "true";
    internal string Root { get; } = root;
    internal Guid Source { get; } = Guid.NewGuid();
    internal Guid Destination { get; } = Guid.NewGuid();
    private readonly Guid sourceIncarnation = Guid.NewGuid();
    private readonly Guid destinationIncarnation = Guid.NewGuid();
    private readonly string cluster = "late-native-" + Guid.NewGuid().ToString("N");
    private readonly string signing = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes));
    private readonly string sourcePeer = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes));
    private readonly string destinationPeer = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes));
    internal NativeLocalhostClusteringOptions LocalhostClustering => new(listeners.SiloEndpoint);
    internal string AdministratorKey { get; } = "root." + Convert.ToHexString(RandomNumberGenerator.GetBytes(SecretBytes));
    internal string Origin(int index) => index < GroupSize ? listeners.ControlOrigins[index]
        : listeners.DestinationOrigins[index - GroupSize];
    internal string DirectoryPath(int index) => Path.Combine(Root, "node" + (index + NodeOrdinalStep).ToString(System.Globalization.CultureInfo.InvariantCulture));

    internal string[] Arguments(int index)
    {
        var source = index < GroupSize;
        var args = new List<string> { PartitionMovementLateNativeProtocol.UrlsArgumentPrefix + Origin(index) };
        void Set(string key, string value) => args.Add(PartitionMovementLateNativeProtocol.NodeArgumentPrefix + key + "=" + value);
        Set(PartitionMovementLateNativeProtocol.DataDirectory, DirectoryPath(index));
        Set(PartitionMovementLateNativeProtocol.PublicEndpoint, Origin(index));
        Set(PartitionMovementLateNativeProtocol.ClusterId, cluster);
        Set(PartitionMovementLateNativeProtocol.PhysicalShardId, (source ? Source : Destination).ToString("D"));
        Set(PartitionMovementLateNativeProtocol.Incarnation, (source ? sourceIncarnation : destinationIncarnation).ToString("D"));
        Set(PartitionMovementLateNativeProtocol.SigningKey, signing);
        Set(PartitionMovementLateNativeProtocol.PeerSecret, source ? sourcePeer : destinationPeer);
        Set(PartitionMovementLateNativeProtocol.AdminKey, AdministratorKey);
        Set(PartitionMovementLateNativeProtocol.SiloAddress, listeners.NativeEndpoint(index).Address.ToString());
        Set(PartitionMovementLateNativeProtocol.SiloPort, listeners.NativeEndpoint(index).Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Set(PartitionMovementLateNativeProtocol.AllowLoopbackHttp, Enabled);
        Set(PartitionMovementLateNativeProtocol.AllowPrivateNetworkHttp, Enabled);
        Set(PartitionMovementLateNativeProtocol.PartitionMovementExecutionEnabled, Enabled);
        Set(PartitionMovementLateNativeProtocol.PhysicalGroupTrustRole, source ? nameof(PhysicalGroupTrustRole.Control) : nameof(PhysicalGroupTrustRole.Destination));
        Set(PartitionMovementLateNativeProtocol.RegisterPhysicalOwners, Enabled);
        Set(PartitionMovementLateNativeProtocol.RemoteDocumentReads, Enabled);
        Set(PartitionMovementLateNativeProtocol.RemotePartitionQueries, Enabled);
        AddScope(args, source);
        for (var voter = FirstVoter; voter < GroupSize; voter++)
        {
            Set(PartitionMovementLateNativeProtocol.Peers + voter.ToString(System.Globalization.CultureInfo.InvariantCulture), Origin((source ? FirstVoter : GroupSize) + voter));
        }
        return args.ToArray();
    }

    private void AddScope(List<string> args, bool source)
    {
        var prefix = PartitionMovementLateNativeProtocol.TrustArgumentPrefix;
        void Set(string key, string value) => args.Add(prefix + key + "=" + value);
        Set(PartitionMovementLateNativeProtocol.ForeignPhysicalShardId, (source ? Destination : Source).ToString("D"));
        Set(PartitionMovementLateNativeProtocol.ForeignIncarnation, (source ? destinationIncarnation : sourceIncarnation).ToString("D"));
        Set(PartitionMovementLateNativeProtocol.ForeignPeerSecret, source ? destinationPeer : sourcePeer);
        for (var voter = FirstVoter; voter < GroupSize; voter++)
        {
            var suffix = voter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Set(PartitionMovementLateNativeProtocol.ForeignVoterIds + suffix, Origin((source ? GroupSize : FirstVoter) + voter));
        }
    }
}
