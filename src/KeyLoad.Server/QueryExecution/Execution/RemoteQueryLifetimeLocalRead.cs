using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.QueryExecution;

internal static class RemoteQueryLifetimeLocalRead
{
    private const int EmptyDetailLength = 0;
    private const int NoFailures = 0;
    internal static Task<PartitionQueryLeafResultV1> QueryAsync(OrleansNode node, PartitionHost partition,
        string principalId, PartitionQueryOwnedLeafRequest request, PartitionQueryLifetimeMetadata metadata,
        DateTimeOffset expiresAt, IResourceLifetimeWorkBinding binding, CancellationToken token)
        => ExecuteAsync<PartitionQueryOwnedLeafRequestV2, PartitionQueryLeafResultV1>(node, partition,
            principalId, new(request, metadata.Definition, metadata.Coverage, metadata.Principal), expiresAt,
            node.CatalogRequestCodec().CreatePartitionQueryLeafV2, binding, token);

    internal static Task<DistributedSearchLeafResultV1> SearchAsync(OrleansNode node, PartitionHost partition,
        string principalId, DistributedSearchOwnedLeafV1 request, PartitionQueryLifetimeMetadata metadata,
        DateTimeOffset expiresAt, IResourceLifetimeWorkBinding binding, CancellationToken token)
        => ExecuteAsync<DistributedSearchOwnedLeafV2, DistributedSearchLeafResultV1>(node, partition,
            principalId, new(request, metadata.Definition, metadata.Coverage, metadata.Principal), expiresAt,
            node.CatalogRequestCodec().CreateDistributedSearchLeafV2, binding, token);

    private static async Task<TResult> ExecuteAsync<TRequest, TResult>(OrleansNode node, PartitionHost partition,
        string principalId, TRequest request, DateTimeOffset expiresAt,
        Func<Guid, string, ReadOnlyMemory<byte>, DateTimeOffset, string> sign, IResourceLifetimeWorkBinding binding,
        CancellationToken token) where TResult : class
    {
        var input = NativeSerialization.Serialize(request);
        var failures = new List<Exception>();
        TResult? result = null;
        IDisposable? identity = null;
        CancellationTokenSource? cancellation = null;
        NativeRequestWorkChildReservation? reservation = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var principal = partition.Database.Store.Read(view => partition.Database.Principal(view,
                principalId, partition.Database.EvaluationClock.GetUtcNow()));
            var requestId = Guid.NewGuid();
            var signed = sign(requestId, principal.Id, input, expiresAt);
            var verified = node.CatalogRequestCodec().VerifyRequest(signed, requestId);
            var parent = RequireParent(binding);
            reservation = parent.ReserveLocalChild(verified,
                node.RuntimeServices.GetRequiredService<PhysicalShardRecord>(),
                node.RuntimeServices.GetRequiredService<SiloStorageIdentityProvider>().Read());
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, reservation.Cancellation);
            identity = node.OpenRequestContext(principal, requestId, Guid.Empty, cancellation.Token);
            var reply = await node.ExecuteAsync(requestId, signed, command: false,
                cancellationToken: cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            result = RequireResult<TResult>(reply);
        }, failures).ConfigureAwait(false);
        var original = failures.Count == NoFailures ? null : failures[NoFailures];
        var cleanup = new List<Exception>();
        if (identity is { } ownedIdentity)
        {
            ServerFailureObserver.Observe(ownedIdentity.Dispose, cleanup);
        }
        if (cancellation is { } ownedCancellation)
        {
            ServerFailureObserver.Observe(ownedCancellation.Dispose, cleanup);
        }
        ServerFailureObserver.Observe(() => CryptographicOperations.ZeroMemory(input), cleanup);
        failures.AddRange(cleanup);
        if (reservation is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
                _ = await reservation.CompleteDispatchAsync(original, cleanup.ToImmutableArray()).ConfigureAwait(false), failures)
                .ConfigureAwait(false);
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

    private static TResult RequireResult<TResult>(GrainOperationReply reply) where TResult : class
    {
        if (reply.Error is { } error)
        {
            if (!Enum.IsDefined(error) || !reply.Payload.IsEmpty
                || reply.SafeDetail is not { Length: > EmptyDetailLength } detail)
            {
                throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
            }
            throw Errors.Fail(error, detail);
        }
        return GrainNativePayload.Read<GrainValue>(reply.Payload).Value as TResult
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }
}
