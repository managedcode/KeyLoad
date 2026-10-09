namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Canonical current control observation; the descriptor retains its original source cut.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveTransferReadAuthorityAlias)]
internal sealed record PartitionMoveTransferReadAuthority(
    [property: Orleans.Id(0)] PartitionMoveParentHeader Header,
    [property: Orleans.Id(1)] PartitionMoveParentPhase CapturePhase,
    [property: Orleans.Id(2)] PartitionMoveControlRecord CurrentControl,
    [property: Orleans.Id(3)] long ControlReadCut);
