using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CommandPhaseAlias)]
internal enum PartitionControlCommandPhase
{
    None = 0,
    Admitted = 1,
    EffectAcknowledged = 2,
    Finalized = 3,
}

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CommandRecordAlias)]
internal sealed record PartitionControlCommandRecord(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] PartitionControlCommandIdentity Identity,
    [property: Orleans.Id(2)] string Fingerprint,
    [property: Orleans.Id(3)] PhysicalShardRecord ControlOwner,
    [property: Orleans.Id(4)] AtomicPartitionPlacementResolution Destination,
    [property: Orleans.Id(5)] Guid EffectId,
    [property: Orleans.Id(6)] long AdmissionPosition,
    [property: Orleans.Id(7)] PartitionControlCommandPhase Phase,
    [property: Orleans.Id(8)] CommitReceipt? TargetEffect,
    [property: Orleans.Id(9)] string? TargetEffectDigest,
    [property: Orleans.Id(10)] PartitionControlOutcomeReference? OriginalOutcome,
    [property: Orleans.Id(11)] OperationResult? OriginalResult,
    [property: Orleans.Id(12)] PartitionControlDelegation? Delegation = null,
    [property: Orleans.Id(13)] ReplicatedOperation? OriginalOperation = null,
    [property: Orleans.Id(14)] ReadOnlyMemory<byte> TargetBody = default,
    [property: Orleans.Id(15)] BlobOutcomeAuthority? BlobAuthority = null);

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CommandEffectAlias)]
internal sealed record PartitionControlEffectPayload(
    [property: Orleans.Id(0)] CommitReceipt Receipt,
    [property: Orleans.Id(1)] OperationResult OriginalResult,
    [property: Orleans.Id(2)] BlobOutcomeAuthority? BlobAuthority = null);
