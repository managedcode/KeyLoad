using KeyLoad.Core;
using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Owns bounded silo-local discovery of canonical ANN maintenance work and joined shutdown.</summary>
/// <param name="id">The native grain-service identity.</param>
/// <param name="silo">The owning Orleans silo.</param>
/// <param name="loggerFactory">The native grain-service logger factory.</param>
/// <param name="services">The actual silo provider; storage-backed dependencies resolve in native sequential Init.</param>
/// <param name="diagnostics">The bounded maintenance failure observer.</param>
public sealed class AnnProjectionGrainService(GrainId id, Silo silo, ILoggerFactory loggerFactory,
    IServiceProvider services,
    ILogger<AnnProjectionGrainService> diagnostics)
    : GrainService(id, silo, loggerFactory), IAnnProjectionGrainService
{
    private DatabaseEngine database = null!;
    private ReplicaConsensus consensus = null!;
    private global::Orleans.IGrainFactory grains = null!;
    private TimeProvider clock = null!;
    private IOptions<DueCoordinationOptions> cadence = null!;
    private RuntimeJournalAdmission runtime = null!;
    private IOptions<DatabaseLimits> limits = null!;

    private readonly Lock producerLifecycle = new();
    private bool producerClosed;
    private Task? producer;

    /// <summary>Initializes the actual silo-owned discovery service.</summary>
    /// <param name="serviceProvider">The original silo provider.</param>
    /// <returns>The native initialization task.</returns>
    public override async Task Init(IServiceProvider serviceProvider)
    {
        await base.Init(serviceProvider).ConfigureAwait(true);
        database = services.GetRequiredService<DatabaseEngine>();
        consensus = services.GetRequiredService<Func<ReplicaConsensus>>()();
        grains = services.GetRequiredService<global::Orleans.IGrainFactory>();
        clock = services.GetRequiredService<TimeProvider>();
        cadence = services.GetRequiredService<IOptions<DueCoordinationOptions>>();
        runtime = services.GetRequiredService<RuntimeJournalAdmission>();
        limits = services.GetRequiredService<IOptions<DatabaseLimits>>();
    }

    /// <summary>Starts the joined canonical post-apply discovery producer.</summary>
    /// <returns>The original native service startup task.</returns>
    protected override Task StartInBackground()
    {
        lock (producerLifecycle)
        {
            if (producerClosed)
            {
                return producer ?? Task.CompletedTask;
            }
            var started = base.StartInBackground();
            producer = RunAsync(StoppedCancellationTokenSource.Token);
            return started;
        }
    }

    /// <summary>Closes local producer admission and borrows its same original terminal task.</summary>
    /// <returns>The original producer, or completion when no producer started before the closed fence.</returns>
    public Task JoinStorageProducerAsync()
    {
        lock (producerLifecycle)
        {
            producerClosed = true;
            return producer ?? Task.CompletedTask;
        }
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
