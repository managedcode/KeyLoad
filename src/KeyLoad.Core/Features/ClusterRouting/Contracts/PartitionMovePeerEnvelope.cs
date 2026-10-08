namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.PeerStageAlias)]
internal enum PartitionMovePeerStage
{
    Fence = 1,
    Capture = 2,
    StagePage = 3,
    Install = 4,
    PublishWitness = 5,
    Retire = 6,
    Abort = 7,
    ControlPrepare = 8,
    ControlAdvance = 9,
    ControlFinalize = 10,
    ControlAuthorize = 11,
    ControlAcknowledge = 12,
    ControlAcceptFence = 13,
    ControlBeginAbort = 14,
    ControlFinalizeAbort = 15,
    ControlCompleteRetirement = 16,
    ControlCancelGrants = 17,
    SourceBeginAbort = 18,
}

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.PeerEnvelopeAlias)]
internal sealed record PartitionMovePeerEnvelope(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid MoveId,
    [property: Orleans.Id(2)] PartitionRef Partition,
    [property: Orleans.Id(3)] PhysicalShardRecord ControlOwner,
    [property: Orleans.Id(4)] AtomicPartitionPlacementResolution SourcePlacement,
    [property: Orleans.Id(5)] PhysicalShardRecord DestinationOwner,
    [property: Orleans.Id(6)] string ControlIntentDigest,
    [property: Orleans.Id(7)] PartitionMovePeerStage Stage,
    [property: Orleans.Id(8)] int PageOrdinal,
    [property: Orleans.Id(9)] DateTimeOffset ExpiresAt,
    [property: Orleans.Id(10)] Guid Nonce,
    [property: Orleans.Id(11)] ReadOnlyMemory<byte> Body,
    [property: Orleans.Id(12)] PartitionMovePhaseGrant? Grant = null);
