using KeyLoad.Clustering;
using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

internal static class PeerDiscoveryNativeColdShutdown
{
    internal static void CloseFailedConstruction(Exception primary, ReplicaConsensus? consensus,
        ReplicaMaterializer? materializer, ReplicaTermMetadataLifetime? journal, string directory)
    {
        List<Exception> failures = [primary];
        if (consensus is not null)
        {
            ReplicaMaterializerLifecycleErrors.Attempt(() => consensus.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures);
        }
        if (materializer is not null)
        {
            ReplicaMaterializerLifecycleErrors.Attempt(() => materializer.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures);
        }
        if (journal is not null)
        {
            ReplicaMaterializerLifecycleErrors.Attempt(journal.Dispose, failures);
        }
        ReplicaMaterializerLifecycleErrors.Attempt(() => Directory.Delete(directory, true), failures);
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }

    internal static async Task StopAllAsync(IEnumerable<PeerDiscoveryNativeNode> nodes, List<Exception> failures,
        CancellationToken token)
    {
        List<Task> originalStops = [];
        foreach (var node in nodes)
        {
            if (node.Runtime is { } runtime)
            {
                Begin(runtime.StopAsync, originalStops, failures);
            }
            Begin(() => node.Consensus.StopAsync(token), originalStops, failures);
        }
        foreach (var original in originalStops)
        {
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(() => original, failures);
        }
    }

    private static void Begin(Func<Task> stop, List<Task> originals, List<Exception> failures)
    {
        try
        {
            originals.Add(stop());
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            ReplicaMaterializerLifecycleErrors.Add(failure, failures);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            ReplicaMaterializerLifecycleErrors.Add(failure, failures);
        }
    }
}
