using KeyLoad.Clustering;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed partial class ControlledPartitionMovementNativeHost
{
    internal Task<GrainOperationReply> SubmitAsync(OperationKind kind, Guid commandId,
        string principalId, ReadOnlyMemory<byte> originalPayload, CancellationToken cancellationToken)
        => ControlledPartitionMovementNativeCommandExecution.SubmitAsync(this, kind, commandId,
            principalId, originalPayload, cancellationToken);
}
