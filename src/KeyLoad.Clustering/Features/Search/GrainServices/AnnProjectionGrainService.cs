using KeyLoad.Core;
using KeyLoad.Replication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Owns bounded silo-local discovery of canonical ANN maintenance work and joined shutdown.</summary>
/// <param name="id">The native grain-service identity.</param>
/// <param name="silo">The owning Orleans silo.</param>
/// <param name="loggerFactory">The native grain-service logger factory.</param>
/// <param name="database">The canonical node-local database owner.</param>
/// <param name="consensus">The physical replica consensus and apply barriers.</param>
/// <param name="grains">The native grain factory for durable maintenance coordinators.</param>
/// <param name="clock">The owning runtime time provider.</param>
/// <param name="diagnostics">The bounded maintenance failure observer.</param>
/// <param name="cadence">The existing due-work cadence bounds.</param>
/// <param name="runtime">The original runtime-journal scheduling admission.</param>
/// <param name="limits">The centrally validated database read bounds.</param>
public sealed class AnnProjectionGrainService(GrainId id, Silo silo, ILoggerFactory loggerFactory,
    DatabaseEngine database, ReplicaConsensus consensus, global::Orleans.IGrainFactory grains,
    TimeProvider clock, ILogger<AnnProjectionGrainService> diagnostics,
    IOptions<DueCoordinationOptions> cadence, RuntimeJournalAdmission runtime,
    IOptions<DatabaseLimits> limits)
    : GrainService(id, silo, loggerFactory), IAnnProjectionGrainService
{
    private Task? producer;

    /// <summary>Initializes the actual silo-owned discovery service.</summary>
    /// <param name="serviceProvider">The original silo provider.</param>
    /// <returns>The native initialization task.</returns>
    public override Task Init(IServiceProvider serviceProvider) => base.Init(serviceProvider);

    /// <summary>Starts the joined canonical post-apply discovery producer.</summary>
    /// <returns>The original native service startup task.</returns>
    protected override Task StartInBackground()
    {
        var started = base.StartInBackground();
        producer = RunAsync(StoppedCancellationTokenSource.Token);
        return started;
    }

    /// <summary>Cancels scheduling and genuinely joins original discovery and base shutdown.</summary>
    /// <returns>The complete owned shutdown task.</returns>
    public override async Task Stop()
    {
        var failures = new List<Exception>();
        Task? stopped = null;
        try
        {
            stopped = base.Stop();
        }
        catch (Exception failure) when (GrainBoundaryErrors.Handles(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!GrainBoundaryErrors.Handles(failure)) { failures.Add(failure); }
        if (stopped is not null)
        {
            await JoinStopAsync(stopped, failures).ConfigureAwait(true);
        }
        if (producer is { } active)
        {
            await JoinStopAsync(active, failures).ConfigureAwait(true);
        }
        AnnProjectionJobFailures.ThrowIfAny(failures);
    }

    private static async Task JoinStopAsync(Task original, List<Exception> failures)
    {
        try
        {
            await original.ConfigureAwait(true);
        }
        catch (Exception failure) when (GrainBoundaryErrors.Handles(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!GrainBoundaryErrors.Handles(failure)) { failures.Add(failure); }
    }

    private async Task RunAsync(CancellationToken stopped)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopped, runtime.SchedulingToken);
        var token = linked.Token;
        byte[]? after = null;
        try
        {
            await consensus.TransportReady.WaitAsync(token).ConfigureAwait(true);
            await runtime.WaitAsync(token).ConfigureAwait(true);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var began = clock.GetTimestamp();
                var applied = consensus.AppliedPosition;
                after = await CycleAsync(after, token).ConfigureAwait(true);
                await RecurringDueWait.WaitForChangeOrFallbackAsync(consensus, applied,
                    cadence.Value.PollInterval, clock, token).ConfigureAwait(true);
                await RecurringDueWait.WaitForMinimumCadenceAsync(began, cadence.Value.MinimumCycleCadence,
                    clock, token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
    }

    private async Task<byte[]?> CycleAsync(byte[]? after, CancellationToken token)
    {
        try
        {
            if (!await consensus.IsLeaderAsync(token).ConfigureAwait(true))
            {
                return null;
            }
            await consensus.ReadBarrierAsync(token).ConfigureAwait(true);
            if (!await consensus.IsLeaderAsync(token).ConfigureAwait(true))
            {
                return null;
            }
            var budget = new ReadExecutionBudget(limits, clock, token);
            var page = database.ReadAnnProjectionWorkPage(after, budget);
            foreach (var rejection in page.Rejected)
            {
                AnnProjectionJobDiagnostics.Failed(diagnostics, rejection);
            }
            foreach (var work in page.Items)
            {
                budget.Check();
                var consumer = work.Consumer.Consumer;
                var generation = work.Consumer.Definition.IndexGeneration;
                var target = grains.GetGrain<IAnnProjectionCoordinatorGrain>(
                    AnnProjectionJobProtocol.GrainKey(consumer, generation, work.NodeId));
                try
                {
                    await JoinAsync(target.ScheduleAsync(consumer, generation, work.NodeId, token), token).ConfigureAwait(true);
                }
                catch (KeyLoadException refusal)
                {
                    AnnProjectionJobDiagnostics.Failed(diagnostics, refusal.Code);
                }
            }
            return page.AfterKey;
        }
        catch (KeyLoadException failure)
        {
            AnnProjectionJobDiagnostics.Failed(diagnostics, failure.Code);
            return after;
        }
        catch (Exception failure) when (!token.IsCancellationRequested && GrainBoundaryErrors.Handles(failure))
        {
            AnnProjectionJobDiagnostics.Failed(diagnostics, ErrorCode.OwnershipLost);
            return after;
        }
    }

    private static async Task JoinAsync(Task original, CancellationToken token)
    {
        try
        {
            await original.WaitAsync(token).ConfigureAwait(true);
        }
        catch (OperationCanceledException initiating) when (token.IsCancellationRequested)
        {
            try
            {
                await original.ConfigureAwait(true);
            }
            catch (Exception cleanup) { throw new AggregateException(initiating, cleanup); }
            throw;
        }
    }
}
