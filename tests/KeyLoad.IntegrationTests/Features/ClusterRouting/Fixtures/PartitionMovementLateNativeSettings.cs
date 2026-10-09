using System.Security.Cryptography;
using KeyLoad.CrashHost.Features.ClusterRouting;

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
        Set(PartitionMovementLateNativeProtocol.AllowLoopbackHttp, Enabled);
        Set(PartitionMovementLateNativeProtocol.AllowPrivateNetworkHttp, Enabled);
        Set(PartitionMovementLateNativeProtocol.PartitionMovementExecutionEnabled, Enabled);
        Set(PartitionMovementLateNativeProtocol.MembershipAuthorityMode, source ? "authority" : "proxy");
        Set(PartitionMovementLateNativeProtocol.MembershipAuthorityRegisterPhysicalOwners, Enabled);
        Set(PartitionMovementLateNativeProtocol.MembershipAuthorityRemoteDocumentReads, Enabled);
        Set(PartitionMovementLateNativeProtocol.MembershipAuthorityRemotePartitionQueries, Enabled);
        AddScope(args, source);
        for (var voter = FirstVoter; voter < GroupSize; voter++)
        { Set(PartitionMovementLateNativeProtocol.Peers + voter.ToString(System.Globalization.CultureInfo.InvariantCulture), Origin((source ? FirstVoter : GroupSize) + voter)); }
        return args.ToArray();
    }

    private void AddScope(List<string> args, bool source)
    {
        var prefix = PartitionMovementLateNativeProtocol.MembershipArgumentPrefix;
        void Set(string key, string value) => args.Add(prefix + key + "=" + value);
        if (source)
        {
            Set(PartitionMovementLateNativeProtocol.TrustedGroupPhysicalShardId, Destination.ToString("D"));
            Set(PartitionMovementLateNativeProtocol.TrustedGroupIncarnation, destinationIncarnation.ToString("D"));
            Set(PartitionMovementLateNativeProtocol.TrustedGroupPeerSecret, destinationPeer);
        }
        else
        {
            Set(PartitionMovementLateNativeProtocol.AuthorityPhysicalShardId, Source.ToString("D"));
            Set(PartitionMovementLateNativeProtocol.AuthorityIncarnation, sourceIncarnation.ToString("D"));
            Set(PartitionMovementLateNativeProtocol.AuthorityPeerSecret, sourcePeer);
        }
        for (var voter = FirstVoter; voter < GroupSize; voter++)
        {
            var suffix = voter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Set((source ? PartitionMovementLateNativeProtocol.TrustedGroupVoterIds : PartitionMovementLateNativeProtocol.AuthorityEndpoints) + suffix, Origin((source ? GroupSize : 0) + voter));
            if (source)
            { Set(PartitionMovementLateNativeProtocol.TrustedGroupSiloEndpoints + suffix, listeners.NativeEndpoint(GroupSize + voter).ToString()); }
        }
    }
}
