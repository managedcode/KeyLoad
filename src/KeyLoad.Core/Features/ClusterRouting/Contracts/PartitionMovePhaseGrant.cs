using System.Collections.Immutable;

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.GrantAlias)]
internal sealed record PartitionMovePhaseGrant(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid GrantId,
    [property: Orleans.Id(2)] Guid PhaseCommandId,
    [property: Orleans.Id(3)] Guid MoveId,
    [property: Orleans.Id(4)] PartitionRef Partition,
    [property: Orleans.Id(5)] PhysicalShardRecord ControlOwner,
    [property: Orleans.Id(6)] PhysicalShardRecord ReceiverOwner,
    [property: Orleans.Id(7)] string OperatorPrincipalId,
    [property: Orleans.Id(8)] long OperatorPolicyEpoch,
    [property: Orleans.Id(9)] PartitionMovePeerStage Stage,
    [property: Orleans.Id(10)] string ControlIntentDigest,
    [property: Orleans.Id(11)] string BodyDigest,
    [property: Orleans.Id(12)] DateTimeOffset ExpiresAt,
    [property: Orleans.Id(13)] long AdmissionPosition,
    [property: Orleans.Id(14)] PartitionMoveJournalReceipt? Settlement,
    [property: Orleans.Id(15)] ImmutableArray<ResourceDefinition> Resources,
    [property: Orleans.Id(16)] PartitionMoveJournalReceipt? AbortDisposition = null,
    [property: Orleans.Id(17)] PartitionMoveCleanupRole? CleanupRole = null,
    [property: Orleans.Id(18)] int? CleanupFamily = null,
    [property: Orleans.Id(19)] Guid? PrecedingGrantId = null,
    [property: Orleans.Id(20)] int PageOrdinal = PartitionMoveProtocol.EmptyCount);

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.GrantBodyAlias)]
internal sealed record PartitionMoveAuthorizeBody(
    [property: Orleans.Id(0)] Guid GrantId,
    [property: Orleans.Id(1)] Guid PhaseCommandId,
    [property: Orleans.Id(2)] string OperatorPrincipalId,
    [property: Orleans.Id(3)] PartitionMovePhaseCommand Phase,
    [property: Orleans.Id(4)] PhysicalShardRecord ReceiverOwner,
    [property: Orleans.Id(5)] DateTimeOffset ExpiresAt);

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.GrantAckBodyAlias)]
internal sealed record PartitionMoveAcknowledgeBody(
    [property: Orleans.Id(0)] Guid GrantId,
    [property: Orleans.Id(1)] PartitionMoveJournalReceipt Settlement);
