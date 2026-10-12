namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Frozen operation domains carried by the native movement service.</summary>
[Alias(PartitionMovementGrainServiceProtocol.ChannelAlias)]
internal enum PartitionMovementGrainServiceChannel
{
    /// <summary>A movement phase command.</summary>
    Phase = PartitionMovementGrainServiceProtocol.PhaseChannel,
    /// <summary>A phase outcome query.</summary>
    Outcome = PartitionMovementGrainServiceProtocol.OutcomeChannel,
    /// <summary>A receiver proof command or query.</summary>
    ReceiverIssue = PartitionMovementGrainServiceProtocol.ReceiverIssueChannel,
    /// <summary>An expired retirement cancellation command or query.</summary>
    RetireCancellation = PartitionMovementGrainServiceProtocol.RetireCancellationChannel,
    /// <summary>A bounded transfer-data operation.</summary>
    TransferData = PartitionMovementGrainServiceProtocol.TransferDataChannel,
    /// <summary>The original bounded native physical-owner probe.</summary>
    PhysicalOwnerProbe = PartitionMovementGrainServiceProtocol.PhysicalOwnerProbeChannel
}
