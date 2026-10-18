using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;

namespace KeyLoad.Clustering;

/// <summary>Buffers time-series hints per native stateless worker activation and flushes on a grain timer.</summary>
[StatelessWorker]
internal sealed class SampleChunkWorkerGrain(DatabaseEngine database, Func<ICommitCoordinator> borrowCoordinator,
    IOptions<BackgroundWorkerOptions> batching, ILogger<SampleChunkWorkerGrain> diagnostics)
    : Grain, ISampleChunkWorkerGrain
{
    private readonly ICommitCoordinator coordinator = borrowCoordinator();
    private readonly Queue<SampleChunkWorkHint> pending = new();
    private byte[]? after;
    private IGrainFactory grains => GrainFactory;

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

    public Task EnqueueAsync(SampleChunkWorkHint hint, CancellationToken cancellationToken)
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
            await ScheduleJoinedAsync(hint, token).ConfigureAwait(true);
            pending.Dequeue();
        }
        after = await CycleAsync(after, token).ConfigureAwait(true);
    }

    private async Task<byte[]?> CycleAsync(byte[]? after, CancellationToken token)
    {
        try
        {
            await coordinator.ReadBarrierAsync(token).ConfigureAwait(true);
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
