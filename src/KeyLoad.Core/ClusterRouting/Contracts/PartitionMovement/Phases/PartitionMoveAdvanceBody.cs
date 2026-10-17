namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.AdvanceBodyAlias)]
internal sealed record PartitionMoveAdvanceBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(2)] PartitionMoveSourceFenceRecord Fence,
    [property: Orleans.Id(3)] PartitionMoveImageDescriptor Descriptor,
    [property: Orleans.Id(4)] CommitReceipt? InstalledReceipt,
    [property: Orleans.Id(5)] Guid? SourceCaptureGrantId = null,
    [property: Orleans.Id(6)] Guid? TargetInstallGrantId = null);
