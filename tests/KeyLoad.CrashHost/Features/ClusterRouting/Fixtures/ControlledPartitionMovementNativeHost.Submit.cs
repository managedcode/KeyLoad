using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed partial class ControlledPartitionMovementNativeHost
{
    private const int NoCommandFailures = 0;

    internal async Task<GrainOperationReply> SubmitAsync(OperationKind kind, Guid commandId,
        string principalId, ReadOnlyMemory<byte> originalPayload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var principal = Store.Read(view => Database.Principal(view, principalId, Clock.GetUtcNow()));
        var requestId = Guid.NewGuid();
        var signed = Silo.CatalogRequestCodec().CreateCommand(requestId, principal.Id,
            kind, commandId, originalPayload);
        GrainOperationReply? originalReply = null;
        var failures = new List<Exception>();
        var observation = new ControlledPartitionMovementNativeCommandObservation(kind, commandId, requestId, Clock);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var identity = Silo.OpenRequestContext(principal, requestId, commandId, cancellationToken);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                observation.Phase = ControlledPartitionMovementNativeCommandObservation.Stage.Execution;
                originalReply = await Silo.ExecuteAsync(requestId, signed, command: true,
                    cancellationToken).ConfigureAwait(false);
                observation.Phase = ControlledPartitionMovementNativeCommandObservation.Stage.ContextSettlement;
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (failures.Count != NoCommandFailures)
        {
            ServerFailureObserver.Observe(() => observation.Report(failures, cancellationToken), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return originalReply ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.Unavailable);
    }
}
