using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Replication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Discovers bounded canonical chunk work on the current RF3 leader and joins original scheduling.</summary>
public sealed class SampleChunkGrainService(GrainId id, Silo silo, ILoggerFactory loggerFactory,
    DatabaseEngine database, ReplicaConsensus consensus, IGrainFactory grains, TimeProvider clock,
    ILogger<SampleChunkGrainService> diagnostics, IOptions<DueCoordinationOptions> execution,
    RuntimeJournalAdmission runtime) : GrainService(id, silo, loggerFactory), ISampleChunkGrainService
{
    private Task? loop;

    /// <summary>Completes native service initialization.</summary>
    /// <param name="serviceProvider">The original silo provider.</param>
    /// <returns>The native initialization task.</returns>
    public override Task Init(IServiceProvider serviceProvider) => base.Init(serviceProvider);

    /// <summary>Starts one joined discovery producer after native lifecycle admission.</summary>
    /// <returns>Original native startup task.</returns>
    protected override Task StartInBackground()
    {
        var started = base.StartInBackground();
        loop = RunAsync(StoppedCancellationTokenSource.Token);
        return started;
    }

    /// <summary>Cancels admission and joins the original producer and native base shutdown.</summary>
    /// <returns>The joined original shutdown.</returns>
    public override async Task Stop()
    {
        var stopped = base.Stop();
        if (loop is { } active)
        { await Task.WhenAll(stopped, active).ConfigureAwait(true); }
        else
        { await stopped.ConfigureAwait(true); }
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
            { return null; }
            await consensus.ReadBarrierAsync(token).ConfigureAwait(true);
            if (!await consensus.IsLeaderAsync(token).ConfigureAwait(true))
            { return null; }
            var page = SampleChunkWorkDiscovery.Read(database, after, token);
            foreach (var hint in page.Hints)
            {
                token.ThrowIfCancellationRequested();
                await ScheduleJoinedAsync(hint, token).ConfigureAwait(true);
            }
            return page.AfterKey;
        }
        catch (KeyLoadException failure)
        { SampleChunkJobDiagnostics.Failed(diagnostics, failure.Code); return after; }
        catch (Exception failure) when (!token.IsCancellationRequested && GrainBoundaryErrors.Handles(failure))
        { SampleChunkJobDiagnostics.Failed(diagnostics, ErrorCode.OwnershipLost); return after; }
    }

    private async Task ScheduleJoinedAsync(SampleChunkWorkHint hint, CancellationToken token)
    {
        var coordinator = grains.GetGrain<ISampleChunkCoordinatorGrain>(hint.Partition.AtomicPartitionId);
        var original = coordinator.ScheduleAsync(hint, token);
        try
        { await original.WaitAsync(token).ConfigureAwait(true); }
        catch (OperationCanceledException initiating) when (token.IsCancellationRequested)
        {
            try
            { await original.ConfigureAwait(true); }
            catch (Exception cleanup) { throw new AggregateException(initiating, cleanup); }
            throw;
        }
    }
}
