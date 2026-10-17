using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Discovers bounded canonical chunk work on the current replicated leader and joins original scheduling.</summary>
public sealed class SampleChunkGrainService(GrainId id, Silo silo, ILoggerFactory loggerFactory,
    IServiceProvider services,
    ILogger<SampleChunkGrainService> diagnostics) : GrainService(id, silo, loggerFactory), ISampleChunkGrainService
{
    private DatabaseEngine database = null!;
    private ReplicaConsensus consensus = null!;
    private IGrainFactory grains = null!;
    private TimeProvider clock = null!;
    private IOptions<DueCoordinationOptions> execution = null!;
    private RuntimeJournalAdmission runtime = null!;

    private readonly Lock producerLifecycle = new();
    private bool producerClosed;
    private Task? loop;

    /// <summary>Completes native service initialization.</summary>
    /// <param name="serviceProvider">The original silo provider.</param>
    /// <returns>The native initialization task.</returns>
    public override async Task Init(IServiceProvider serviceProvider)
    {
        await base.Init(serviceProvider).ConfigureAwait(true);
        database = services.GetRequiredService<DatabaseEngine>();
        consensus = services.GetRequiredService<Func<ReplicaConsensus>>()();
        grains = services.GetRequiredService<IGrainFactory>();
        clock = services.GetRequiredService<TimeProvider>();
        execution = services.GetRequiredService<IOptions<DueCoordinationOptions>>();
        runtime = services.GetRequiredService<RuntimeJournalAdmission>();
    }

    /// <summary>Starts one joined discovery producer after native lifecycle admission.</summary>
    /// <returns>Original native startup task.</returns>
    protected override Task StartInBackground()
    {
        lock (producerLifecycle)
        {
            if (producerClosed)
            {
                return loop ?? Task.CompletedTask;
            }
            var started = base.StartInBackground();
            loop = RunAsync(StoppedCancellationTokenSource.Token);
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
            return loop ?? Task.CompletedTask;
        }
    }

    /// <summary>Cancels admission and joins the original producer and native base shutdown.</summary>
    /// <returns>The joined original shutdown.</returns>
    public override async Task Stop()
    {
        var stopped = base.Stop();
        if (loop is { } active)
        {
            await Task.WhenAll(stopped, active).ConfigureAwait(true);
        }
        else
        {
            await stopped.ConfigureAwait(true);
        }
    }

    private async Task RunAsync(CancellationToken stopped)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopped, runtime.SchedulingToken);
        var token = linked.Token;
        byte[]? after = null;
        try
        {
            await consensus.TransportReady.WaitAsync(token).ConfigureAwait(true);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var began = clock.GetTimestamp();
                var applied = consensus.AppliedPosition;
                after = await CycleAsync(after, token).ConfigureAwait(true);
                await RecurringDueWait.WaitForChangeOrFallbackAsync(consensus, applied,
                    execution.Value.PollInterval, clock, token).ConfigureAwait(true);
                await RecurringDueWait.WaitForMinimumCadenceAsync(began, execution.Value.MinimumCycleCadence,
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
            var page = SampleChunkWorkDiscovery.Read(database, after, token);
            foreach (var hint in page.Hints)
            {
                token.ThrowIfCancellationRequested();
                await ScheduleJoinedAsync(hint, token).ConfigureAwait(true);
            }
            return page.AfterKey;
        }
        catch (KeyLoadException failure)
        {
            SampleChunkJobDiagnostics.Failed(diagnostics, failure.Code);
            return after;
        }
        catch (Exception failure) when (!token.IsCancellationRequested && GrainBoundaryErrors.Handles(failure))
        {
            SampleChunkJobDiagnostics.Failed(diagnostics, ErrorCode.OwnershipLost);
            return after;
        }
    }

    private async Task ScheduleJoinedAsync(SampleChunkWorkHint hint, CancellationToken token)
    {
        var coordinator = grains.GetGrain<ISampleChunkCoordinatorGrain>(hint.Partition.AtomicPartitionId);
        var original = coordinator.ScheduleAsync(hint, token);
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
