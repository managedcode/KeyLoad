using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

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
        var settings = options.PhysicalGroupTrust;
        return settings.Role switch
        {
            PhysicalGroupTrustRole.Control => Local(options, configuration),
            PhysicalGroupTrustRole.Destination => Entry(settings.ForeignPhysicalShardId,
                settings.ForeignIncarnation, settings.ForeignVoterIds.ToImmutableArray()),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, PhysicalOwnerProbeProtocol.Unavailable)
        };
    }

    internal static RegisteredPhysicalOwnerV1 Destination(NodeOptions options, PartitionHost partition)
        => Destination(options, partition.Configuration);

    internal static RegisteredPhysicalOwnerV1 Destination(NodeOptions options, ReplicaConfiguration configuration)
    {
        var settings = options.PhysicalGroupTrust;
        return settings.Role switch
        {
            PhysicalGroupTrustRole.Control => Entry(settings.ForeignPhysicalShardId,
                settings.ForeignIncarnation, settings.ForeignVoterIds.ToImmutableArray()),
            PhysicalGroupTrustRole.Destination => Local(options, configuration),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, PhysicalOwnerProbeProtocol.Unavailable)
        };
    }

    internal static RegisteredPhysicalOwnerV1 ForVoter(NodeOptions options, ReplicaConfiguration configuration, string voter)
    {
        var local = Local(options, configuration);
        if (local.Owner.VoterIds.Contains(voter, StringComparer.Ordinal))
        {
            return local;
        }
        if (options.PhysicalGroupTrust.Role == PhysicalGroupTrustRole.Disabled)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        }
        var foreign = options.PhysicalGroupTrust.Role == PhysicalGroupTrustRole.Control
            ? Destination(options, configuration) : Control(options, configuration);
        return foreign.Owner.VoterIds.Contains(voter, StringComparer.Ordinal)
            ? foreign : throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
    }

    private static RegisteredPhysicalOwnerV1 Entry(Guid physical, Guid incarnation, ImmutableArray<string> voters)
        => new(new(physical, incarnation, voters, PhysicalOwnerDirectoryProtocol.InitialEpoch),
            voters.Select(value => new Uri(value, UriKind.Absolute).AbsoluteUri).ToImmutableArray());
}
