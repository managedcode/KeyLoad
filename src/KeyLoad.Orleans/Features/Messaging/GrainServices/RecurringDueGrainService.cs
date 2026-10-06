using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Per-silo leader watcher which reads canonical due hints and drains them through partition grains.</summary>
/// <param name="id">The Orleans grain-service identity for this silo instance.</param>
/// <param name="silo">The hosting Orleans silo.</param>
/// <param name="loggerFactory">The factory used by the grain-service base class.</param>
/// <param name="database">The node-local database and canonical read owner.</param>
/// <param name="consensus">The local replicated-partition leadership and read-barrier service.</param>
/// <param name="grainFactory">The Orleans factory for partition coordinator activations.</param>
/// <param name="clock">The shared UTC clock used for due discovery and bounded polling.</param>
/// <param name="diagnostics">Safe operational diagnostics for rejected pages and dispatch outcomes.</param>
/// <param name="options">The centrally validated dispatch and discovery scheduling settings.</param>
/// <param name="journalAdmission">The native scheduling admission and early shutdown token.</param>
public sealed class RecurringDueGrainService(GrainId id, Silo silo,
    Microsoft.Extensions.Logging.ILoggerFactory loggerFactory,
    DatabaseEngine database, ReplicaConsensus consensus, IGrainFactory grainFactory, TimeProvider clock,
    Microsoft.Extensions.Logging.ILogger<RecurringDueGrainService> diagnostics,
    IOptions<DueCoordinationOptions> options, RuntimeJournalAdmission journalAdmission)
    : GrainService(id, silo, loggerFactory), IRecurringDueGrainService
{
    private const int NoRejectedDueHints = 0;
    private Task? loop;

    /// <summary>Completes native per-silo grain-service initialization.</summary>
    /// <param name="serviceProvider">The active silo service provider.</param>
    /// <returns>The base grain-service initialization task.</returns>
    public override Task Init(IServiceProvider serviceProvider) => base.Init(serviceProvider);

    /// <summary>Starts the joined due-discovery loop after native grain-service startup.</summary>
    /// <returns>The base grain-service startup task.</returns>
    protected override Task StartInBackground()
    {
        var started = base.StartInBackground();
        loop = RunAsync(StoppedCancellationTokenSource.Token);
        return started;
    }

    /// <summary>Stops admission, cancels discovery and joins the active loop.</summary>
    /// <returns>The joined base and due-loop shutdown task.</returns>
    public override async Task Stop()
    {
        var stopped = base.Stop();
        if (loop is { } active)
        {
            await Task.WhenAll(stopped, active).ConfigureAwait(true);
            return;
        }
        await stopped.ConfigureAwait(true);
    }

    private async Task RunAsync(CancellationToken stopped)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stopped, journalAdmission.SchedulingToken);
        var cancellationToken = lifetime.Token;
        DueSweepCursor? cursor = null;
        try
        {
            await consensus.TransportReady.WaitAsync(cancellationToken).ConfigureAwait(true);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cycleStarted = clock.GetTimestamp();
                var observedPosition = consensus.AppliedPosition;
                cursor = await RunCycleAsync(cursor, cancellationToken).ConfigureAwait(true);
                await RecurringDueWait.WaitForChangeOrFallbackAsync(consensus, observedPosition,
                    options.Value.PollInterval, clock, cancellationToken).ConfigureAwait(true);
                await RecurringDueWait.WaitForMinimumCadenceAsync(cycleStarted, options.Value.MinimumCycleCadence,
                    clock, cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (KeyLoadException failure)
        {
            RecurringDueDiagnostics.ServiceFault(diagnostics, failure.Code);
        }
        catch (OperationCanceledException)
        {
            RecurringDueDiagnostics.ServiceFault(diagnostics, ErrorCode.OwnershipLost);
        }
        catch (Exception failure) when (GrainBoundaryErrors.Handles(failure))
        {
            RecurringDueDiagnostics.ServiceFault(diagnostics, ErrorCode.OwnershipLost);
        }
    }

    private async Task<DueSweepCursor?> RunCycleAsync(DueSweepCursor? cursor,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await ReadAndDispatchPage(cursor, cancellationToken).ConfigureAwait(true);
            cursor = page.Cursor;
            if (page.Rejected.Length > NoRejectedDueHints)
            {
                RecurringDueDiagnostics.PageRejected(diagnostics, page.Rejected.Length);
            }
        }
        catch (KeyLoadException failure) when (failure.Code == ErrorCode.Corruption
            && failure.Message == DueWorkProtocol.KeyExceedsBound)
        {
            cursor = DueWorkCursor.DeferNext(database.Store.Identity, cursor);
            RecurringDueDiagnostics.PrefixRejected(diagnostics, failure.Code);
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

    private async Task<DueWorkPage> ReadAndDispatchPage(DueSweepCursor? cursor,
        CancellationToken cancellationToken)
    {
        if (!await consensus.IsLeaderAsync(cancellationToken).ConfigureAwait(true))
        {
            return DueWorkDiscovery.EmptyPage(cursor, database.Store.Identity);
        }
        await consensus.ReadBarrierAsync(cancellationToken).ConfigureAwait(true);
        if (!await consensus.IsLeaderAsync(cancellationToken).ConfigureAwait(true))
        {
            return DueWorkDiscovery.EmptyPage(cursor, database.Store.Identity);
        }
        var page = DueWorkDiscovery.ReadPage(database, cursor, clock.GetUtcNow(), cancellationToken);
        foreach (var hint in page.Jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Dispatch(hint, cancellationToken).ConfigureAwait(true);
        }
        return page;
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
