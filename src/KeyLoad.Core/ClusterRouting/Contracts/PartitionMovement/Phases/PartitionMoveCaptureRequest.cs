namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CaptureRequestAlias)]
internal sealed record PartitionMoveCaptureRequest(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveSourceFenceRecord Fence,
    [property: Orleans.Id(2)] int MaximumPageBytes,
    [property: Orleans.Id(3)] long MaximumImageBytes,
    [property: Orleans.Id(4)] int MaximumRecords);
