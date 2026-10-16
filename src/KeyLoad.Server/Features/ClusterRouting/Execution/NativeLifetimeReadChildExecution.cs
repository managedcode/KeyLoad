using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class NativeLifetimeReadChildExecution
{
    private const int NoFailures = 0;

    internal static async Task<TResult> ExecuteAsync<TResult>(OrleansNode node, PrincipalRecord principal,
        Guid requestId, string signed, ReadOnlyMemory<byte>? ownedInput, IResourceLifetimeWorkBinding binding,
        CancellationToken token, Action<GrainOperationReply>? observeReply = null,
        Guid? connectionId = null) where TResult : class
    {
        var failures = new List<Exception>();
        var cleanup = new List<Exception>();
        NativeRequestWorkChildReservation? reservation = null;
        CancellationTokenSource? cancellation = null;
        IDisposable? identity = null;
        TResult? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var verified = node.CatalogRequestCodec().VerifyRequest(signed, requestId);
            var parent = RequireParent(binding);
            reservation = parent.ReserveLocalChild(verified,
                node.RuntimeServices.GetRequiredService<PhysicalShardRecord>(),
                node.RuntimeServices.GetRequiredService<ReplicaSiloDiscoveryState>().Read());
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, reservation.Cancellation);
            identity = node.OpenRequestContext(principal, requestId, Guid.Empty, cancellation.Token, connectionId);
            var reply = await node.ExecuteAsync(requestId, signed, command: false,
                cancellationToken: cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            observeReply?.Invoke(reply);
            result = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as TResult
                ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
        }, failures).ConfigureAwait(false);
        var original = failures.Count == NoFailures ? null : failures[NoFailures];
        ServerFailureObserver.Observe(() => identity?.Dispose(), cleanup);
        ServerFailureObserver.Observe(() => cancellation?.Dispose(), cleanup);
        if (ownedInput is { } input)
        {
            ServerFailureObserver.Observe(() => CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(input).Span), cleanup);
        }
        failures.AddRange(cleanup);
        if (reservation is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var report = await reservation.CompleteDispatchAsync(original, cleanup.ToImmutableArray()).ConfigureAwait(false);
                if (report.CleanupOrFatalFailure is { } failure)
                {
                    throw failure;
                }
            }, failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }

    private static NativeRequestWorkLease RequireParent(IResourceLifetimeWorkBinding binding)
    {
        if (binding is not INativeRequestWorkBinding native
            || !ReferenceEquals(binding.ResultRetention, native.OriginalProducer.ResultRetention))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.InvalidProof);
        }
        return native.OriginalProducer;
    }
}
