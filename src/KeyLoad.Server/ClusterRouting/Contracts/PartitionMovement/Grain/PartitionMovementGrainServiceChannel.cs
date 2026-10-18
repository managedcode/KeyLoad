namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Frozen operation domains carried by the native owner service.</summary>
[Alias(PartitionMovementGrainServiceProtocol.ChannelAlias)]
internal enum PartitionMovementGrainServiceChannel
{
    /// <summary>The original bounded native physical-owner probe.</summary>
    PhysicalOwnerProbe = PartitionMovementGrainServiceProtocol.PhysicalOwnerProbeChannel
}
