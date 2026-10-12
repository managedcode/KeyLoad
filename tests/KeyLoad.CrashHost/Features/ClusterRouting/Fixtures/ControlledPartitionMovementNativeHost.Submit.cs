using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed partial class ControlledPartitionMovementNativeHost
{
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
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var identity = Silo.OpenRequestContext(principal, requestId, commandId, cancellationToken);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                originalReply = await Silo.ExecuteAsync(requestId, signed, command: true,
                    cancellationToken).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return originalReply ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.Unavailable);
    }
}
