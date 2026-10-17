using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementContractNames.TransferAuthorityResultAlias)]
internal sealed record PartitionMovementTransferAuthorityResult(
    [property: global::Orleans.Id(0)] PartitionMoveTransferReadAuthority Authority,
    [property: global::Orleans.Id(1)] long ReadBytes,
    [property: global::Orleans.Id(2)] int ExaminedRecords);
