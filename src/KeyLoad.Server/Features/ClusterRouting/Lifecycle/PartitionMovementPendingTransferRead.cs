using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal sealed class PartitionMovementPendingTransferRead(PartitionRef partition, Guid moveId)
    : PartitionMovementPendingSourceRead<PartitionMovementTransferHandle>(partition, moveId);
