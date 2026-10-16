using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.QueryExecution;

internal static class RemoteQueryLifetimeLocalRead
{
    private const int EmptyDetailLength = 0;
    internal static Task<PartitionQueryLeafResultV1> QueryAsync(OrleansNode node, PartitionHost partition,
        string principalId, PartitionQueryOwnedLeafRequest request, PartitionQueryLifetimeMetadata metadata,
        DateTimeOffset expiresAt, CancellationToken token)
        => ExecuteAsync<PartitionQueryOwnedLeafRequestV2, PartitionQueryLeafResultV1>(node, partition,
            principalId, new(request, metadata.Definition, metadata.Coverage, metadata.Principal), expiresAt,
            node.CatalogRequestCodec().CreatePartitionQueryLeafV2, token);

    internal static Task<DistributedSearchLeafResultV1> SearchAsync(OrleansNode node, PartitionHost partition,
        string principalId, DistributedSearchOwnedLeafV1 request, PartitionQueryLifetimeMetadata metadata,
        DateTimeOffset expiresAt, CancellationToken token)
        => ExecuteAsync<DistributedSearchOwnedLeafV2, DistributedSearchLeafResultV1>(node, partition,
            principalId, new(request, metadata.Definition, metadata.Coverage, metadata.Principal), expiresAt,
            node.CatalogRequestCodec().CreateDistributedSearchLeafV2, token);

    private static async Task<TResult> ExecuteAsync<TRequest, TResult>(OrleansNode node, PartitionHost partition,
        string principalId, TRequest request, DateTimeOffset expiresAt,
        Func<Guid, string, ReadOnlyMemory<byte>, DateTimeOffset, string> sign, CancellationToken token) where TResult : class
    {
        var input = NativeSerialization.Serialize(request);
        var failures = new List<Exception>();
        TResult? result = null;
        IDisposable? identity = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var principal = partition.Database.Store.Read(view => partition.Database.Principal(view,
                principalId, partition.Database.EvaluationClock.GetUtcNow()));
            var requestId = Guid.NewGuid();
            var signed = sign(requestId, principal.Id, input, expiresAt);
            identity = node.OpenRequestContext(principal, requestId, Guid.Empty, token);
            var reply = await node.ExecuteAsync(requestId, signed, command: false, cancellationToken: token)
                .ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (reply.Error is { } error)
            {
                if (!Enum.IsDefined(error) || !reply.Payload.IsEmpty || reply.SafeDetail is not { Length: > EmptyDetailLength } detail)
                {
                    throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
                }
                throw Errors.Fail(error, detail);
            }
            result = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as TResult
                ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
        }, failures).ConfigureAwait(false);
        if (identity is { } ownedIdentity)
        {
            ServerFailureObserver.Observe(ownedIdentity.Dispose, failures);
        }
        ServerFailureObserver.Observe(() => CryptographicOperations.ZeroMemory(input), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }
}
