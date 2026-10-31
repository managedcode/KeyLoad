using KeyLoad.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;

namespace KeyLoad.Clustering;

/// <summary>Activation-local ANN hints; canonical records remain the durable work source.</summary>
[StatelessWorker(MaximumLocalPollers)]
internal sealed class AnnProjectionWorkerGrain(BackgroundStorageClient storage,
    IServiceProvider services, TimeProvider clock,
    IOptions<BackgroundWorkerOptions> batching, ILogger<AnnProjectionWorkerGrain> diagnostics)
    : Grain, IAnnProjectionWorkerGrain
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

    /// <summary>A refused or unreachable hint leaves the buffer; the persisted projection record remains the durable source.</summary>
    private async Task<byte[]?> CycleAsync(byte[]? after, CancellationToken token)
    {
        try
        {
            return await AnnProjectionJobOperation.RunAsync<byte[]?>(services,
                binding => CycleCoreAsync(after, binding), token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return after;
        }
    }

    private async Task<byte[]?> CycleCoreAsync(byte[]? after, CapabilityWorkLifetime binding)
    {
        var token = binding.Token;
        try
        {
            var budget = new ReadExecutionBudget(clock, token);
            budget.AttachResourceLifetimeBinding(binding);
            var targetOwner = await storage.AnnDiscoveryTargetAsync(token).ConfigureAwait(true);
            var page = await BackgroundStorageReadAccounting.ReadAsync<AnnProjectionDiscoveryPage>(budget, binding,
                cancellation => storage.Operations.ReadAnnProjectionWorkPageAsync(targetOwner, after,
                    cancellation), token: token).ConfigureAwait(true);
            foreach (var rejection in page.Rejected)
            {
                AnnProjectionJobDiagnostics.Failed(diagnostics, rejection);
            }
            foreach (var work in page.Hints)
            {
                budget.Check();
                var consumer = work.Consumer;
                var generation = work.Generation;
                var target = grains.GetGrain<IAnnProjectionCoordinatorGrain>(
                    AnnProjectionJobProtocol.GrainKey(consumer, generation, work.NodeId));
                try
                {
                    binding.OriginalProducer.AdmitPublication();
                    await JoinAsync(target.ScheduleAsync(consumer, generation, work.NodeId, token), token).ConfigureAwait(true);
                }
                catch (KeyLoadException refusal)
                {
                    AnnProjectionJobDiagnostics.Failed(diagnostics, refusal.Code);
                }
            }
            return page.AfterKey?.ToArray();
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
