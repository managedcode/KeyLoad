using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CommandEffectAlias)]
internal sealed record PartitionControlEffectPayload(
    [property: Orleans.Id(0)] CommitReceipt Receipt,
    [property: Orleans.Id(1)] OperationResult OriginalResult,
    [property: Orleans.Id(2)] BlobOutcomeAuthority? BlobAuthority = null);
