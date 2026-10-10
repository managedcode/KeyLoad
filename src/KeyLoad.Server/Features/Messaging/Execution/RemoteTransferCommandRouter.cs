using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Messaging;

internal sealed class RemoteTransferCommandRouter(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> options, IOptions<DatabaseLimits> limits, RemoteDocumentClient client,
    RemoteDocumentWorkOwner owner, TimeProvider clock, IControlledDocumentCommandRouter? ordinary)
    : IControlledDocumentCommandRouter
{
    public async Task<OperationResult?> TryExecuteAsync(GrainRequestEnvelope envelope,
        ReadOnlyMemory<byte> payload, CancellationToken cancellationToken,
        Func<CancellationToken, ValueTask>? grantSettled = null,
        Func<CancellationToken, ValueTask>? outcomeObserved = null)
    {
        if (envelope.CommandKind != OperationKind.Batch)
        {
            return ordinary is null ? null : await ordinary.TryExecuteAsync(envelope, payload,
            cancellationToken, grantSettled, outcomeObserved).ConfigureAwait(false);
        }
        var failures = new List<Exception>();
        OperationResult? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var operation = owner.Acquire(envelope.RequestId);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var expiry = OriginalExpiry(envelope.ExpiresAt);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                    operation.ShutdownToken, expiry.Token);
                result = await ExecuteAtCutAsync(envelope, payload, linked.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? (ordinary is null ? null : await ordinary.TryExecuteAsync(envelope, payload,
            cancellationToken, grantSettled, outcomeObserved).ConfigureAwait(false));
    }

    private async Task<OperationResult?> ExecuteAtCutAsync(GrainRequestEnvelope envelope,
        ReadOnlyMemory<byte> payload, CancellationToken token)
    {
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        node.CatalogRequestCodec().ValidateScope(envelope);
        var principal = GrainRequestAuthority.ReloadForRequest(partition.Database, envelope, clock);
        var work = new ReadExecutionBudget(limits, clock, token);
        var original = partition.Database.CreateNativeOperation(OperationKind.Batch,
            envelope.CommandId, principal.Id, clock.GetUtcNow(), payload);
        var context = partition.Database.CaptureRemoteTransferDispatch(principal.Id, original, work);
        if (context is null)
        { return null; }
        var call = RemoteTransferSourceCall.Create(node, partition, options.Value, envelope,
            context, RemoteQueueTransferPeerStage.Accept, limits.Value.MaxBatchBytes);
        var result = await client.TransferAsync(call, token).ConfigureAwait(false);
        try
        {
            await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
            var currentPrincipal = GrainRequestAuthority.ReloadForRequest(partition.Database, envelope, clock);
            var current = partition.Database.CaptureRemoteTransferDispatch(currentPrincipal.Id, original, work)
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Unavailable);
            DatabaseEngine.RequireRemoteTransferSameDispatch(context, current);
            work.Check();
        }
        catch (KeyLoadException error) when (error.Code is ErrorCode.PermissionDenied
            or ErrorCode.Unauthenticated or ErrorCode.TokenInvalidated)
        {
            var unknown = Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteTransferPeerProtocol.Unknown);
            unknown.Data[RemoteTransferPeerProtocol.OriginalAuthorizationFailure] = error;
            throw unknown;
        }
        RemoteTransferPeerShape.RequireResult(result, RemoteQueueTransferPeerStage.Accept);
        return result.OriginalOutcome!.Result
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid);
    }

    private CancellationTokenSource OriginalExpiry(DateTimeOffset expiresAt)
    {
        var remaining = expiresAt - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid); }
        return new(remaining, clock);
    }
}
