using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Replication;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PhysicalOwnerConfiguredTuples
{
    internal static RegisteredPhysicalOwnerV1 Local(NodeOptions options, PartitionHost partition)
        => Local(options, partition.Configuration);

    internal static RegisteredPhysicalOwnerV1 Local(NodeOptions options, ReplicaConfiguration configuration)
        => Entry(options.PhysicalShardId, configuration.Incarnation, configuration.VoterIds);

    internal static RegisteredPhysicalOwnerV1 Control(NodeOptions options, PartitionHost partition)
        => Control(options, partition.Configuration);

    internal static RegisteredPhysicalOwnerV1 Control(NodeOptions options, ReplicaConfiguration configuration)
    {
        var settings = options.MembershipAuthority;
        return settings.Mode switch
        {
            MembershipAuthoritySettingsProtocol.Authority => Local(options, configuration),
            MembershipAuthoritySettingsProtocol.Proxy => Entry(settings.AuthorityPhysicalShardId,
                settings.AuthorityIncarnation, settings.AuthorityEndpoints.ToImmutableArray()),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, PhysicalOwnerProbeProtocol.Unavailable)
        };
    }

    internal static RegisteredPhysicalOwnerV1 Destination(NodeOptions options, PartitionHost partition)
        => Destination(options, partition.Configuration);

    internal static RegisteredPhysicalOwnerV1 Destination(NodeOptions options, ReplicaConfiguration configuration)
    {
        var settings = options.MembershipAuthority;
        return settings.Mode switch
        {
            MembershipAuthoritySettingsProtocol.Authority => Entry(settings.TrustedGroupPhysicalShardId,
                settings.TrustedGroupIncarnation, settings.TrustedGroupVoterIds.ToImmutableArray()),
            MembershipAuthoritySettingsProtocol.Proxy => Local(options, configuration),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, PhysicalOwnerProbeProtocol.Unavailable)
        };
    }

    private static RegisteredPhysicalOwnerV1 Entry(Guid physical, Guid incarnation, ImmutableArray<string> voters)
        => new(new(physical, incarnation, voters, PhysicalOwnerDirectoryProtocol.InitialEpoch),
            voters.Select(value => new Uri(value, UriKind.Absolute).AbsoluteUri).ToImmutableArray());
}
