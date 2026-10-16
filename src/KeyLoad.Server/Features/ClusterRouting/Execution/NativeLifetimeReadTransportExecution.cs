using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class NativeLifetimeReadTransportExecution
{
    private const int NoFailures = 0;

    internal static bool Handles(RemoteModelNativeChannel channel)
        => channel is RemoteModelNativeChannel.ResourceLifetimeRead
            or RemoteModelNativeChannel.ResourceLifetimeAnnRead or RemoteModelNativeChannel.ResourceLifetimeBlobOutcome
            or RemoteModelNativeChannel.EventVector;

    internal static async Task<RemoteModelNativeReply> ExecuteAsync(NativeRequestWorkOwner owner,
        DatabaseEngine database, RemoteModelNativeRequest request, CancellationToken caller, CancellationToken shutdown,
        Func<IResourceLifetimeWorkBinding, CancellationToken, Task<RemoteModelNativeReply>> execute)
    {
        var failures = new List<Exception>();
        NativeRequestWorkLease? lease = null;
        ResourceLifetimeWorkResultRetention? ledger = null;
        CancellationTokenSource? linked = null;
        RemoteModelNativeReply? reply = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            database.RequireResourceLifetimeWorkFenceAttached();
            ledger = new(database.OperationLimitsOptions);
            lease = owner.Acquire(request.Nonce, NativeRequestWorkKind.NativeTransport);
            lease.AttachResultRetention(ledger);
            linked = CancellationTokenSource.CreateLinkedTokenSource(caller, shutdown, owner.ShutdownToken, lease.Cancellation);
            reply = await execute(lease, linked.Token).ConfigureAwait(false);
            if (reply.Error is null)
            {
                linked.Token.ThrowIfCancellationRequested();
                lease.AdmitPublication();
            }
        }, failures).ConfigureAwait(false);
        await FinishAsync(lease, ledger, linked, failures).ConfigureAwait(false);
        if (failures.Count != NoFailures && reply is not null)
        {
            ServerFailureObserver.Observe(() => ClearOwnedReply(reply), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return reply ?? throw Errors.Fail(ErrorCode.Corruption, RemoteModelNativeProtocol.InvalidProof);
    }

    private static void ClearOwnedReply(RemoteModelNativeReply reply) => ClearOwnedPayload(reply.Payload);

    internal static void ClearOwnedPayload(ReadOnlyMemory<byte> payload)
    {
        if (!MemoryMarshal.TryGetArray(payload, out var owned))
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteModelNativeProtocol.InvalidProof);
        }
        CryptographicOperations.ZeroMemory(owned.AsSpan());
    }

    internal static async Task FinishAsync(NativeRequestWorkLease? lease,
        ResourceLifetimeWorkResultRetention? ledger, CancellationTokenSource? linked, List<Exception> failures)
    {
        var cleanup = new List<Exception>();
        var original = failures.Count == NoFailures ? null : failures[NoFailures];
        var prepared = lease is null;
        if (lease is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await lease.PrepareCompletionAsync().ConfigureAwait(false);
                prepared = true;
            }, cleanup).ConfigureAwait(false);
        }
        ServerFailureObserver.Observe(() => linked?.Dispose(), cleanup);
        if (prepared)
        {
            ServerFailureObserver.Observe(() => ledger?.Dispose(), cleanup);
        }
        failures.AddRange(cleanup);
        if (lease is not null && prepared)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var outcome = await lease.CompleteAsync(original,
                    cleanup.Count == NoFailures ? null : new AggregateException(cleanup)).ConfigureAwait(false);
                if (outcome.CleanupOrJoinFailure is { } failure)
                {
                    throw failure;
                }
            }, failures).ConfigureAwait(false);
        }
    }
}
