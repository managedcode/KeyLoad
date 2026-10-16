using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphNativeWorkExecution
{
    private const int NoFailures = 0;

    internal static async Task<RemoteGraphNativeReplyV1> ExecuteAsync(NativeRequestWorkOwner owner,
        DatabaseEngine database, RemoteGraphNativeRequestV1 request, CancellationToken token,
        Func<IResourceLifetimeWorkBinding, CancellationToken, Task<RemoteGraphNativeReplyV1>> execute)
    {
        var failures = new List<Exception>();
        NativeRequestWorkLease? lease = null;
        ResourceLifetimeWorkResultRetention? ledger = null;
        CancellationTokenSource? linked = null;
        RemoteGraphNativeReplyV1? reply = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            database.RequireResourceLifetimeWorkFenceAttached();
            ledger = new(database.OperationLimitsOptions);
            lease = owner.Acquire(request.Nonce, NativeRequestWorkKind.NativeTransport);
            lease.AttachResultRetention(ledger);
            linked = CancellationTokenSource.CreateLinkedTokenSource(token, owner.ShutdownToken, lease.Cancellation);
            reply = await execute(lease, linked.Token).ConfigureAwait(false);
            if (reply.Error is null)
            {
                linked.Token.ThrowIfCancellationRequested();
                lease.AdmitPublication();
            }
        }, failures).ConfigureAwait(false);
        await NativeLifetimeReadTransportExecution.FinishAsync(lease, ledger, linked, failures).ConfigureAwait(false);
        if (failures.Count != NoFailures && reply is not null)
        {
            ServerFailureObserver.Observe(() =>
                NativeLifetimeReadTransportExecution.ClearOwnedPayload(reply.Payload), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return reply ?? throw Errors.Fail(ErrorCode.Corruption, RemoteGraphNativeProtocol.InvalidProof);
    }
}
