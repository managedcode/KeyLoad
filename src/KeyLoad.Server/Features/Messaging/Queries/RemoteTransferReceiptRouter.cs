using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Messaging;

internal sealed class RemoteTransferReceiptRouter(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> options, IOptions<DatabaseLimits> limits, RemoteDocumentClient client,
    RemoteDocumentWorkOwner owner, TimeProvider clock)
{
    internal async Task<RemoteQueueTransferReceiptRead> ReadAsync(GrainRequestEnvelope envelope,
        PrincipalRecord principal, InspectQueueTransferReceiptRequest request, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        RemoteQueueTransferReceiptRead? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var operation = owner.Acquire(envelope.RequestId);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var expiry = OriginalExpiry(envelope.ExpiresAt);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                    operation.ShutdownToken, expiry.Token);
                result = await ReadAtCutAsync(envelope, principal, request, linked.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid);
    }

    private async Task<RemoteQueueTransferReceiptRead> ReadAtCutAsync(GrainRequestEnvelope envelope,
        PrincipalRecord principal, InspectQueueTransferReceiptRequest request, CancellationToken token)
    {
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        node.CatalogRequestCodec().ValidateScope(envelope);
        var currentPrincipal = GrainRequestAuthority.ReloadForRequest(partition.Database, envelope, clock);
        if (currentPrincipal.Id != principal.Id)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteTransferPeerProtocol.Invalid); }
        var work = new ReadExecutionBudget(limits, clock, token);
        var context = partition.Database.CaptureRemoteTransferReceiptDispatch(principal.Id, request, work);
        if (context is null)
        { return new(false, null); }
        var call = RemoteTransferSourceCall.Create(node, partition, options.Value, envelope,
            context, RemoteQueueTransferPeerStage.Receipt, limits.Value.MaxBatchBytes);
        var result = await client.TransferAsync(call, token).ConfigureAwait(false);
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        currentPrincipal = GrainRequestAuthority.ReloadForRequest(partition.Database, envelope, clock);
        var current = partition.Database.CaptureRemoteTransferReceiptDispatch(currentPrincipal.Id, request, work)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Unavailable);
        DatabaseEngine.RequireRemoteTransferSameDispatch(context, current);
        RemoteTransferPeerShape.RequireResult(result, RemoteQueueTransferPeerStage.Receipt);
        if (result.Receipt is null)
        { return new(true, null); }
        var receipt = partition.Database.MintRemoteTransferReceiptInspection(currentPrincipal.Id, request,
            context, call.SourceOwner, result.Receipt, work);
        work.Check();
        return new(true, receipt);
    }

    private CancellationTokenSource OriginalExpiry(DateTimeOffset expiresAt)
    {
        var remaining = expiresAt - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid); }
        return new(remaining, clock);
    }
}
