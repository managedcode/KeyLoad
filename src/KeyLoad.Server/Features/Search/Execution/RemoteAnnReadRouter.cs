using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed class RemoteAnnReadRouter(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> options, IOptions<DatabaseLimits> limits, IOptions<QueryExecutionOptions> configured,
    RemoteDocumentWorkOwner owner,
    TimeProvider clock) : IRemoteAnnReadRouter
{
    public async Task<object> ReadAnnAsync(GrainRequestEnvelope envelope, PrincipalRecord principal,
        GrainReadKind kind, ReadOnlyMemory<byte> payload, SearchEngine actualSearch, CancellationToken cancellationToken)
    {
        var execution = configured.Value;
        if (!execution.EnableApproximateSearch)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, AnnSearchProtocol.Disabled);
        }
        var selected = RemoteAnnReadSelection.Decode(kind, payload);
        var work = new ReadExecutionBudget(limits, clock, cancellationToken);
        QueryResultBudgetPolicy.Constrain(work, execution);
        work.ConstrainLifetime(envelope.ExpiresAt);
        work.ConstrainNativeWork(work.MaximumNativeReadBytes, work.MaximumNativeScanRecords);
        work.ChargeBytes(payload.Length);
        var captured = partition.Database.CaptureRemoteAnnRead(principal.Id, selected.Consumer, selected.Collection,
            selected.Field, selected.Space, selected.Generation,
            PhysicalOwnerConfiguredTuples.Local(options.Value, partition), work);
        if (captured is null || captured.OriginalMaintenance.NodeId == partition.Database.Store.Identity.NodeId)
        {
            return await selected.ExecuteLocalAsync(actualSearch, principal.Id, work, cancellationToken)
                .ConfigureAwait(false);
        }
        var failures = new List<Exception>();
        object? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var operation = owner.Acquire(envelope.RequestId);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var remaining = envelope.ExpiresAt - clock.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionObservationProtocol.Invalid);
                }
                using var deadline = new CancellationTokenSource(remaining, clock);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                    operation.ShutdownToken, deadline.Token);
                using var cancellation = work.EnterStageCancellation(linked.Token);
                var original = captured.OriginalMaintenance;
                var exchange = new AnnProjectionNativeExchange(node, partition, options, clock, captured, original);
                var locator = await AnnProjectionNativeLocator.FindAsync(exchange, captured.Destination,
                    principal.Id, original, envelope.ExpiresAt, work, linked.Token).ConfigureAwait(false);
                AnnProjectionObservationSource.RequireSame(partition, captured, original, work);
                var request = selected.ForOriginal(original, work);
                var observed = await exchange.ReadAsync(captured.Destination, locator.VoterIndex, principal.Id,
                    request, envelope.ExpiresAt, work, linked.Token).ConfigureAwait(false);
                AnnProjectionNativeLocator.RequireResult(locator, original, observed);
                await partition.Coordinator.ReadBarrierAsync(linked.Token).ConfigureAwait(false);
                AnnProjectionObservationSource.RequireSame(partition, captured, original, work);
                linked.Token.ThrowIfCancellationRequested();
                result = selected.ReadResult(observed.Result);
                work.CheckResult(result);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.RecoveryRequired, AnnProjectionObservationProtocol.Invalid);
    }
}
