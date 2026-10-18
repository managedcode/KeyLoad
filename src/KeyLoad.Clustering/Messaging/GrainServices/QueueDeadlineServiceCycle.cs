using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class QueueDeadlineServiceCycle
{
    private const int NoRejectedRecords = 0;

    internal static async Task<QueueDeadlineCursor?> RunAsync(BackgroundStorageClient storage,
        IGrainFactory factory, TimeProvider clock, ILogger<RecurringDueWorkerGrain> diagnostics,
        IOptions<DueCoordinationOptions> options, QueueDeadlineCursor? cursor, CancellationToken cancellationToken)
    {
        try
        {
            var discovery = await storage.Operations.ReadQueueDeadlinePageAsync(cursor, cancellationToken).ConfigureAwait(true);
            cursor = discovery.Cursor;
            if (discovery.PrefixError is { } prefixError)
            {
                RecurringDueDiagnostics.PrefixRejected(diagnostics, prefixError);
                return cursor;
            }
            var page = discovery.Page ?? throw Errors.Fail(ErrorCode.Corruption, DueCoordinatorFields.InvalidHint);
            if (page.Rejected.Length > NoRejectedRecords)
            {
                RecurringDueDiagnostics.PageRejected(diagnostics, page.Rejected.Length);
            }
            foreach (var hint in page.Jobs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DispatchAsync(factory, hint, clock, diagnostics, options, cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (KeyLoadException failure)
        {
            RecurringDueDiagnostics.ServiceFault(diagnostics, failure.Code);
        }
        catch (Exception failure) when (GrainBoundaryErrors.Handles(failure))
        {
            RecurringDueDiagnostics.ServiceFault(diagnostics, ErrorCode.OwnershipLost);
        }
        return cursor;
    }

    private static async Task DispatchAsync(IGrainFactory factory, QueueDeadlineHint hint, TimeProvider clock,
        ILogger<RecurringDueWorkerGrain> diagnostics, IOptions<DueCoordinationOptions> options,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(options.Value.DispatchDeadline, clock);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var owner = factory.GetGrain<IRecurringDueCoordinatorGrain>(hint.Lane.Partition.AtomicPartitionId);
            var dispatch = owner.ProcessQueueDeadlineAsync(hint, lifetime.Token);
            var result = await AwaitDispatchAsync(dispatch, lifetime.Token).ConfigureAwait(true);
            if (result.Error is { } code)
            {
                RecurringDueDiagnostics.DispatchFailed(diagnostics, code);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            RecurringDueDiagnostics.DispatchDeadline(diagnostics);
        }
        catch (KeyLoadException failure) when (!cancellationToken.IsCancellationRequested)
        {
            RecurringDueDiagnostics.DispatchFailed(diagnostics, failure.Code);
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested && GrainBoundaryErrors.Handles(failure))
        {
            RecurringDueDiagnostics.DispatchFailed(diagnostics, ErrorCode.OwnershipLost);
        }
    }

    private static async Task<DueDispatchResult> AwaitDispatchAsync(Task<DueDispatchResult> dispatch,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dispatch.WaitAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await dispatch.ConfigureAwait(true);
            throw;
        }
    }
}
