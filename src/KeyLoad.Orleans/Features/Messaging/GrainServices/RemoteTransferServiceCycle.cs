using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal static class RemoteTransferServiceCycle
{
    internal static async Task<RemoteTransferPendingCursor?> RunAsync(DatabaseEngine database, ReplicaConsensus consensus,
        IGrainFactory grains, TimeProvider clock, Microsoft.Extensions.Logging.ILogger diagnostics,
        IOptions<DueCoordinationOptions> options, RemoteTransferPendingCursor? cursor, CancellationToken token)
    {
        var next = cursor;
        try
        {
            if (!await consensus.IsLeaderAsync(token).ConfigureAwait(true))
            { return cursor; }
            await consensus.ReadBarrierAsync(token).ConfigureAwait(true);
            if (!await consensus.IsLeaderAsync(token).ConfigureAwait(true))
            { return cursor; }
            var subject = options.Value.TransferCoordinatorPrincipalId;
            if (subject is null)
            { return cursor; }
            var page = RemoteTransferPendingDiscovery.Read(database, subject, cursor, token);
            next = page.Cursor;
            if (page.Hint is { } hint)
            { await DispatchAsync(grains, hint, clock, options, token).ConfigureAwait(true); }
            return page.Cursor;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) when (token.IsCancellationRequested) { throw; }
        catch (KeyLoadException failure) { RecurringDueDiagnostics.ServiceFault(diagnostics, failure.Code); }
        catch (Exception failure) when (GrainBoundaryErrors.Handles(failure))
        { RecurringDueDiagnostics.ServiceFault(diagnostics, ErrorCode.OwnershipLost); }
        return next;
    }

    private static async Task DispatchAsync(IGrainFactory grains, RemoteTransferCoordinationHint hint, TimeProvider clock,
        IOptions<DueCoordinationOptions> options, CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(options.Value.DispatchDeadline, clock);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var task = grains.GetGrain<IRecurringDueCoordinatorGrain>(hint.Source.Partition.AtomicPartitionId)
            .ProcessQueueTransferAsync(hint, lifetime.Token);
        try
        {
            var result = await task.WaitAsync(lifetime.Token).ConfigureAwait(true);
            if (result.Error is { } error)
            { throw Errors.Fail(error, RemoteTransferCoordinationProtocol.InvalidResult); }
        }
        catch (OperationCanceledException cancellation) when (lifetime.IsCancellationRequested)
        {
            try
            { await task.ConfigureAwait(true); }
            catch (Exception settlement) when (GrainBoundaryErrors.Handles(settlement))
            { throw new AggregateException(RemoteTransferCoordinationProtocol.InvalidResult, cancellation, settlement); }
            throw;
        }
    }
}
