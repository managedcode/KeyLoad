using KeyLoad.Clustering;
using Orleans.Services;

namespace KeyLoad.Server;

/// <summary>Joins original producers and replica drain before the one late physical close.</summary>
internal sealed class PartitionStorageLifecycle(PartitionHostApplicationOwner owner, IServiceProvider services)
    : ILifecycleParticipant<ISiloLifecycle>
{
    private const string LifecycleName = "KeyLoad.PartitionStorage";
    private const string ProducerCaptureLifecycleSuffix = ".CaptureProducers";
    private const int NoOriginalFailures = 0;
    private const int CaptureProducersStage = ServiceLifecycleStage.AfterRuntimeGrainServices;
    private const int AfterReplicaDrainStage = ServiceLifecycleStage.RuntimeStorageServices - 1;
    private readonly Lock gate = new();
    private Task? closing;

    public void Participate(ISiloLifecycle lifecycle)
    {
        lifecycle.Subscribe(LifecycleName + ProducerCaptureLifecycleSuffix, CaptureProducersStage, CaptureProducersAsync);
        lifecycle.Subscribe(LifecycleName, AfterReplicaDrainStage, _ => Task.CompletedTask, StopAsync);
    }

    private Task CaptureProducersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Native stage 8000 has already constructed and sequentially initialized these same services.
        var nativeServices = services.GetServices<IGrainService>().ToArray();
        var recurring = nativeServices.OfType<RecurringDueGrainService>().Single();
        var chunks = nativeServices.OfType<SampleChunkGrainService>().Single();
        var ann = nativeServices.OfType<AnnProjectionGrainService>().Single();
        owner.CaptureStorageProducers(recurring.JoinStorageProducerAsync,
            chunks.JoinStorageProducerAsync, ann.JoinStorageProducerAsync);
        return Task.CompletedTask;
    }

    internal Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (gate)
        {
            return closing ??= CloseCoreAsync();
        }
    }

    private async Task CloseCoreAsync()
    {
        // Retain the original close before cancellation callbacks can synchronously reenter.
        await Task.Yield();
        var failures = new List<Exception>();
        // The same terminal owner joins connections before node/producers/transport, including DI fallback.
        await ServerFailureObserver.ObserveAsync(() => owner.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ThrowOriginalFailures(failures);
    }

    private static void ThrowOriginalFailures(IEnumerable<Exception> failures)
    {
        var originals = new List<Exception>();
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        foreach (var failure in failures)
        {
            CollectOriginal(failure, originals, seen);
        }
        ServerFailureObserver.ThrowIfAny(originals);
    }

    private static void CollectOriginal(Exception failure, List<Exception> originals, HashSet<Exception> seen)
    {
        if (!seen.Add(failure))
        {
            return;
        }
        if (failure is AggregateException aggregate && aggregate.InnerExceptions.Count > NoOriginalFailures)
        {
            foreach (var original in aggregate.InnerExceptions)
            {
                CollectOriginal(original, originals, seen);
            }
        }
        else
        {
            originals.Add(failure);
        }
    }
}
