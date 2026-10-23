using KeyLoad.Core.Features.TimeSeries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;

namespace KeyLoad.Clustering;

/// <summary>Buffers time-series hints per execution stateless worker activation and flushes on a grain timer.</summary>
[StatelessWorker(MaximumLocalPollers)]
internal sealed class SampleChunkWorkerGrain(BackgroundStorageClient storage,
    IOptions<BackgroundWorkerOptions> batching, ILogger<SampleChunkWorkerGrain> diagnostics)
    : Grain, ISampleChunkWorkerGrain
{
    /// <summary>One polling activation per silo; a second poller would only re-read the same persisted pages.</summary>
    private const int MaximumLocalPollers = 1;
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

    private async Task FlushAsync(CancellationToken token)
    {
        after = await CycleAsync(after, token).ConfigureAwait(true);
    }

    /// <summary>A refused or unreachable hint leaves the buffer; the persisted chunk record remains the durable source.</summary>
    private async Task<byte[]?> CycleAsync(byte[]? after, CancellationToken token)
    {
        try
        {
            var page = await storage.Operations.ReadSampleChunkWorkPageAsync(after, token).ConfigureAwait(true);
            foreach (var hint in page.Hints)
            {
                token.ThrowIfCancellationRequested();
                await ScheduleJoinedAsync(hint, token).ConfigureAwait(true);
            }
            return page.AfterKey?.ToArray();
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
