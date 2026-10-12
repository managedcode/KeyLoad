using System.Globalization;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed partial class ControlledPartitionMovementNativeHostSettings
{
    private const string NodeArgumentPrefix = "--KeyLoad:";
    private const string UrlArgumentPrefix = "--urls=";
    private const string KeySeparator = ":";
    private const string Assignment = "=";
    private const string Enabled = "true";
    private const int FirstVoter = 0;

    internal string[] Arguments(int index)
    {
        var control = index < FirstDestination;
        var local = control ? Control.Owner : Destination.Owner;
        var foreign = control ? Destination.Owner : Control.Owner;
        var arguments = new List<string> { UrlArgumentPrefix + origins[index] };
        void Set(string key, string value) => arguments.Add(NodeArgumentPrefix + key + Assignment + value);
        Set(nameof(NodeOptions.DataDirectory), DirectoryPath(index));
        Set(nameof(NodeOptions.PublicEndpoint), origins[index]);
        Set(nameof(NodeOptions.ClusterId), cluster);
        Set(nameof(NodeOptions.PhysicalShardId), local.PhysicalShardId.ToString(IdentityGuidFormat));
        Set(nameof(NodeOptions.Incarnation), local.Incarnation.ToString(IdentityGuidFormat));
        Set(nameof(NodeOptions.SigningKey), signing);
        Set(nameof(NodeOptions.PeerSecret), control ? controlPeer : destinationPeer);
        Set(nameof(NodeOptions.AdminKey), AdministratorKey);
        Set(nameof(NodeOptions.SiloAddress), silos[index].Address.ToString());
        Set(nameof(NodeOptions.SiloPort), silos[index].Port.ToString(CultureInfo.InvariantCulture));
        Set(nameof(NodeOptions.AllowLoopbackHttp), Enabled);
        Set(nameof(NodeOptions.AllowPrivateNetworkHttp), Enabled);
        Set(PartitionMovementExecutionOptions.SectionName + KeySeparator + nameof(PartitionMovementExecutionOptions.Enabled), Enabled);
        var trust = nameof(NodeOptions.PhysicalGroupTrust) + KeySeparator;
        Set(trust + nameof(PhysicalGroupTrustOptions.Role),
            (control ? PhysicalGroupTrustRole.Control : PhysicalGroupTrustRole.Destination).ToString());
        Set(trust + nameof(PhysicalGroupTrustOptions.RegisterPhysicalOwners), Enabled);
        Set(trust + nameof(PhysicalGroupTrustOptions.ForeignPhysicalShardId), foreign.PhysicalShardId.ToString(IdentityGuidFormat));
        Set(trust + nameof(PhysicalGroupTrustOptions.ForeignIncarnation), foreign.Incarnation.ToString(IdentityGuidFormat));
        Set(trust + nameof(PhysicalGroupTrustOptions.ForeignPeerSecret), control ? destinationPeer : controlPeer);
        for (var voter = FirstVoter; voter < ControlledPartitionMovementNativeHostEndpoints.GroupSize; voter++)
        {
            var ordinal = voter.ToString(CultureInfo.InvariantCulture);
            Set(nameof(NodeOptions.Peers) + KeySeparator + ordinal, local.VoterIds[voter]);
            Set(trust + nameof(PhysicalGroupTrustOptions.ForeignVoterIds) + KeySeparator + ordinal, foreign.VoterIds[voter]);
        }
        return arguments.ToArray();
    }
}
