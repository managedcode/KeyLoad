using KeyLoad.Orleans;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.QueryExecution;

internal sealed class RemoteDistributedSearchRouter(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> options, IOptions<QueryExecutionOptions> execution, RemoteDocumentClient client,
    RemoteDocumentWorkOwner owner, TimeProvider clock) : IRemoteDistributedSearchRouter
{
    public async Task<DistributedSearchPageV1> ReadAsync(GrainRequestEnvelope envelope, PrincipalRecord principal,
        DistributedSearchRequestV1 request, CancellationToken cancellationToken,
        Func<CancellationToken, Task>? statisticsCaptured = null)
    {
        var failures = new List<Exception>();
        DistributedSearchPageV1? page = null;
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
                var reads = new RemoteDistributedSearchLeafRead(node, partition, options, client);
                page = await DistributedSearchCoordinator.ExecuteAsync(partition.Database, request, execution,
                    (work, grant, budget) => reads.Prepare(envelope, principal.Id, work, grant, budget),
                    reads.RevalidateAsync, clock, envelope.ExpiresAt, original.Token, statisticsCaptured).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return page ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }
}
