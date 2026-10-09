using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Retains the original Capture producer under the shared joined source-read lifetime.</summary>
internal sealed class PartitionMovementPendingCapture(PartitionRef partition, Guid moveId)
    : PartitionMovementPendingSourceRead<PartitionMovementCaptureHandle>(partition, moveId);
