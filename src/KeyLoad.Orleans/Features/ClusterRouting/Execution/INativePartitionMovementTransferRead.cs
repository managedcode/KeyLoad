using KeyLoad.Core;

namespace KeyLoad.Orleans;

/// <summary>Borrowed current source-read owner, independent of original Capture effect permission.</summary>
internal interface INativePartitionMovementTransferRead
{
    Task<PartitionMovementTransferHandle> OpenAsync(PrincipalRecord principal,
        PartitionMovementTransferDataCapability query, ReadExecutionBudget work, CancellationToken cancellationToken);
    Task<PartitionMovementPageResult> ReadPageAsync(PrincipalRecord principal,
        PartitionMovementTransferDataCapability query, ReadExecutionBudget work, CancellationToken cancellationToken);
    Task<PartitionMovementTransferClosed> CloseAsync(PrincipalRecord principal,
        PartitionMovementTransferDataCapability query, ReadExecutionBudget work, CancellationToken cancellationToken);
}
