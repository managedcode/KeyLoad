using KeyLoad.Orleans;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.QueryExecution;

internal sealed class RemotePartitionQueryRouter(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> options, RemoteDocumentClient client, RemoteDocumentWorkOwner owner, TimeProvider clock)
    : IRemotePartitionQueryRouter
{
    public async Task<PartitionQueryPageV1> ReadAsync(GrainRequestEnvelope envelope, PrincipalRecord principal,
        PartitionQueryRequestV1 request, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        PartitionQueryPageV1? page = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var operation = owner.Acquire(envelope.RequestId);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var remaining = envelope.ExpiresAt - clock.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteDocumentProtocol.InvalidProof); }
                using var expiry = new CancellationTokenSource(remaining, clock);
                using var original = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                    operation.ShutdownToken, expiry.Token);
                var queries = node.BorrowPartitionQueries();
                var reads = new RemotePartitionQueryLeafRead(node, partition, options, client, queries);
                page = await PartitionQueryRemoteExecution.ExecuteAsync(queries, request,
                    (plan, grant, budget) => reads.Prepare(envelope, principal.Id, plan, grant, budget),
                    token => reads.RevalidateAsync(principal.Id, token), clock, original.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return page ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }
}
