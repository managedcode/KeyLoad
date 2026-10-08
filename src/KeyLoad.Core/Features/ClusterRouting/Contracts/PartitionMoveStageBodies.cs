namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.PrepareBodyAlias)]
internal sealed record PartitionMovePrepareBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveRequest Request);

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.ControlBodyAlias)]
internal sealed record PartitionMoveControlBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveControlRecord Control);

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.PageBodyAlias)]
internal sealed record PartitionMovePageBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(2)] PartitionMoveSourceFenceRecord Fence,
    [property: Orleans.Id(3)] PartitionMoveImageDescriptor Descriptor,
    [property: Orleans.Id(4)] PartitionMoveImagePage Page);

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.InstallBodyAlias)]
internal sealed record PartitionMoveInstallBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(2)] PartitionMoveSourceFenceRecord Fence,
    [property: Orleans.Id(3)] PartitionMoveImageDescriptor Descriptor);

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.AdvanceBodyAlias)]
internal sealed record PartitionMoveAdvanceBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(2)] PartitionMoveSourceFenceRecord Fence,
    [property: Orleans.Id(3)] PartitionMoveImageDescriptor Descriptor,
    [property: Orleans.Id(4)] CommitReceipt? InstalledReceipt,
    [property: Orleans.Id(5)] Guid? SourceCaptureGrantId = null,
    [property: Orleans.Id(6)] Guid? TargetInstallGrantId = null);

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CaptureRequestAlias)]
internal sealed record PartitionMoveCaptureRequest(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveSourceFenceRecord Fence,
    [property: Orleans.Id(2)] int MaximumPageBytes,
    [property: Orleans.Id(3)] long MaximumImageBytes,
    [property: Orleans.Id(4)] int MaximumRecords);
