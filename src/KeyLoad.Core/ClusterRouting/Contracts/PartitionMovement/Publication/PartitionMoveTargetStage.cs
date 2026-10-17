namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.TargetStageAlias)]
internal sealed record PartitionMoveTargetStage(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(2)] PartitionMoveSourceFenceRecord Fence,
    [property: Orleans.Id(3)] PartitionMoveImageDescriptor Descriptor,
    [property: Orleans.Id(4)] int AcceptedPages,
    [property: Orleans.Id(5)] long RetainedBytes,
    [property: Orleans.Id(6)] bool Installed,
    [property: Orleans.Id(7)] bool Published,
    [property: Orleans.Id(8)] int InstalledPages = PartitionMoveProtocol.EmptyCount,
    [property: Orleans.Id(9)] CommitToken? InstalledToken = null);
