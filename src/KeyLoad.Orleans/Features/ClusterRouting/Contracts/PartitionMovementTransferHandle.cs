using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Orleans;

/// <summary>Verified current pages under a fresh read lifetime and the original canonical descriptor.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementContractNames.TransferHandleAlias)]
internal sealed record PartitionMovementTransferHandle(
    [property: global::Orleans.Id(0)] Guid HandleId,
    [property: global::Orleans.Id(1)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(2)] PartitionMoveImageDescriptor OriginalDescriptor,
    [property: global::Orleans.Id(3)] int PageCount,
    [property: global::Orleans.Id(4)] long CurrentReadCut,
    [property: global::Orleans.Id(5)] long ReadBytes,
    [property: global::Orleans.Id(6)] int RetainedRecords);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementContractNames.TransferClosedAlias)]
internal sealed record PartitionMovementTransferClosed(
    [property: global::Orleans.Id(0)] Guid HandleId,
    [property: global::Orleans.Id(1)] PartitionMovementTransferCloseDisposition Disposition);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementContractNames.TransferCloseDispositionAlias)]
internal enum PartitionMovementTransferCloseDisposition { Joined = 1, AlreadyAbsent = 2 }
