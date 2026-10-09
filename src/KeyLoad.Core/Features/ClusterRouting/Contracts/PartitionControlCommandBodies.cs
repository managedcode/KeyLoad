using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CommandAdmitBodyAlias)]
internal sealed record PartitionControlAdmitBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(2)] ReplicatedOperation OriginalOperation,
    [property: Orleans.Id(3)] Guid EffectId,
    [property: Orleans.Id(4)] DateTimeOffset ExpiresAt);

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CommandAckBodyAlias)]
internal sealed record PartitionControlAcknowledgeBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionControlCommandIdentity Identity,
    [property: Orleans.Id(2)] Guid EffectId,
    [property: Orleans.Id(3)] CommitReceipt TargetReceipt,
    [property: Orleans.Id(4)] OperationResult OriginalResult,
    [property: Orleans.Id(5)] Guid TargetGrantId,
    [property: Orleans.Id(6)] BlobOutcomeAuthority? BlobAuthority = null);

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CommandFinalizeBodyAlias)]
internal sealed record PartitionControlFinalizeBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionControlCommandIdentity Identity,
    [property: Orleans.Id(2)] Guid EffectId);

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CommandApplyBodyAlias)]
internal sealed record PartitionControlApplyBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(2)] PartitionControlDelegation Delegation,
    [property: Orleans.Id(3)] CommandRequest? Command);
