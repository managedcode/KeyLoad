using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;

namespace KeyLoad.Clustering;

/// <summary>Bounded queue events are flushed by execution timers; persisted due records survive activation loss.</summary>
[StatelessWorker]
internal sealed class RecurringDueWorkerGrain(BackgroundStorageClient storage,
    TimeProvider clock, IOptions<DueCoordinationOptions> options, IOptions<BackgroundWorkerOptions> batching,
    ILogger<RecurringDueWorkerGrain> diagnostics) : Grain, IRecurringDueWorkerGrain
{
    private readonly Queue<DueWorkHint> pending = new();
    private DueSweepCursor? due;
    private QueueDeadlineCursor? deadlines;
    private RemoteTransferPendingCursor? transfers;
    private IGrainFactory grainFactory => GrainFactory;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        batching.Value.Validate();
        this.RegisterGrainTimer(FlushAsync, new GrainTimerCreationOptions
        {
            DueTime = batching.Value.FlushInterval,
            Period = batching.Value.FlushInterval,
            KeepAlive = true
        });
        return base.OnActivateAsync(cancellationToken);
    }

    public Task WakeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task EnqueueAsync(DueWorkHint hint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pending.Count >= batching.Value.MaximumBufferedEvents)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, BackgroundWorkerOptions.BufferFull);
        }
        pending.Enqueue(hint);
        return Task.CompletedTask;
    }

    private async Task FlushAsync(CancellationToken token)
    {
        while (pending.TryPeek(out var hint))
        {
            await Dispatch(hint, token).ConfigureAwait(true);
            pending.Dequeue();
        }
        due = await RunCycleAsync(due, token).ConfigureAwait(true);
        deadlines = await RunQueueCycleAsync(deadlines, token).ConfigureAwait(true);
        transfers = await RunTransferCycleAsync(transfers, token).ConfigureAwait(true);
    }

    private Task<RemoteTransferPendingCursor?> RunTransferCycleAsync(RemoteTransferPendingCursor? cursor, CancellationToken token)
        => RemoteTransferServiceCycle.RunAsync(storage, grainFactory, clock, diagnostics, options, cursor, token);

    private Task<QueueDeadlineCursor?> RunQueueCycleAsync(QueueDeadlineCursor? cursor, CancellationToken cancellationToken)
        => QueueDeadlineServiceCycle.RunAsync(storage, grainFactory, clock, diagnostics, options, cursor, cancellationToken);

    private async Task<DueSweepCursor?> RunCycleAsync(DueSweepCursor? cursor,
        CancellationToken cancellationToken)
    {
        try
        {
            var discovery = await ReadAndDispatchPage(cursor, cancellationToken).ConfigureAwait(true);
            cursor = discovery.Cursor;
            if (discovery.PrefixError is { } error)
            {
                RecurringDueDiagnostics.PrefixRejected(diagnostics, error);
            }
            else if (discovery.Page!.Rejected.Length > 0)
            {
                RecurringDueDiagnostics.PageRejected(diagnostics, discovery.Page.Rejected.Length);
            }
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

    private async Task<DueWorkDiscoveryResult> ReadAndDispatchPage(DueSweepCursor? cursor,
        CancellationToken cancellationToken)
    {
        var discovery = await storage.Operations.ReadDueWorkPageAsync(cursor, cancellationToken).ConfigureAwait(true);
        if (discovery.PrefixError is not null)
        {
            return discovery;
        }
        var page = discovery.Page ?? throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidWakeInstant);
        foreach (var hint in page.Jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Dispatch(hint, cancellationToken).ConfigureAwait(true);
        }
        return discovery;
    }

    private async Task Dispatch(DueWorkHint hint, CancellationToken cancellationToken)
    {
        var dispatchDeadline = options.Value.DispatchDeadline;
        using var timeout = new CancellationTokenSource(dispatchDeadline, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var partition = hint.Lane.Partition.AtomicPartitionId;
            var coordinator = grainFactory.GetGrain<IRecurringDueCoordinatorGrain>(partition);
            var dispatch = coordinator.ProcessDueAsync(hint, deadline.Token);
            var result = await AwaitDispatch(dispatch, deadline.Token).ConfigureAwait(true);
            if (result.Error is { } code)
            {
                RecurringDueDiagnostics.DispatchFailed(diagnostics, code);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            RecurringDueDiagnostics.DispatchDeadline(diagnostics);
        }
        catch (KeyLoadException failure) when (!cancellationToken.IsCancellationRequested)
        {
            RecurringDueDiagnostics.DispatchFailed(diagnostics, failure.Code);
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested
            && GrainBoundaryErrors.Handles(failure))
        {
            RecurringDueDiagnostics.DispatchFailed(diagnostics, ErrorCode.OwnershipLost);
        }
    }

    private static async Task<DueDispatchResult> AwaitDispatch(Task<DueDispatchResult> dispatch,
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
