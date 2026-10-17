namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.PageBodyAlias)]
internal sealed record PartitionMovePageBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(2)] PartitionMoveSourceFenceRecord Fence,
    [property: Orleans.Id(3)] PartitionMoveImageDescriptor Descriptor,
    [property: Orleans.Id(4)] PartitionMoveImagePage Page);
