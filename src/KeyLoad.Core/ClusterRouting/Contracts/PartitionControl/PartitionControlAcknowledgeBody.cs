using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CommandAckBodyAlias)]
internal sealed record PartitionControlAcknowledgeBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionControlCommandIdentity Identity,
    [property: Orleans.Id(2)] Guid EffectId,
    [property: Orleans.Id(3)] CommitReceipt TargetReceipt,
    [property: Orleans.Id(4)] OperationResult OriginalResult,
    [property: Orleans.Id(5)] Guid TargetGrantId,
    [property: Orleans.Id(6)] BlobOutcomeAuthority? BlobAuthority = null);
