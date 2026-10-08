using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PhysicalOwnerConfiguredTuples
{
    internal static RegisteredPhysicalOwnerV1 Local(NodeOptions options, PartitionHost partition)
        => Entry(options.PhysicalShardId, partition.Configuration.Incarnation, partition.Configuration.VoterIds);

    internal static RegisteredPhysicalOwnerV1 Control(NodeOptions options, PartitionHost partition)
    {
        var settings = options.MembershipAuthority;
        return settings.Mode switch
        {
            MembershipAuthoritySettingsProtocol.Authority => Local(options, partition),
            MembershipAuthoritySettingsProtocol.Proxy => Entry(settings.AuthorityPhysicalShardId,
                settings.AuthorityIncarnation, settings.AuthorityEndpoints.ToImmutableArray()),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, PhysicalOwnerProbeProtocol.Unavailable)
        };
    }

    internal static RegisteredPhysicalOwnerV1 Destination(NodeOptions options, PartitionHost partition)
    {
        var settings = options.MembershipAuthority;
        return settings.Mode switch
        {
            MembershipAuthoritySettingsProtocol.Authority => Entry(settings.TrustedGroupPhysicalShardId,
                settings.TrustedGroupIncarnation, settings.TrustedGroupVoterIds.ToImmutableArray()),
            MembershipAuthoritySettingsProtocol.Proxy => Local(options, partition),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, PhysicalOwnerProbeProtocol.Unavailable)
        };
    }

    private static RegisteredPhysicalOwnerV1 Entry(Guid physical, Guid incarnation, ImmutableArray<string> voters)
        => new(new(physical, incarnation, voters, PhysicalOwnerDirectoryProtocol.InitialEpoch),
            voters.Select(value => new Uri(value, UriKind.Absolute).AbsoluteUri).ToImmutableArray());
}
