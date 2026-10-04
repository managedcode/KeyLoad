using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

/// <summary>Owns closed real CrashHost source/target stores and observes readiness before deleting their root.</summary>
internal sealed class ReplicaFileReadinessFixture : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly ReplicaFileReadinessStores stores;
    private Task? readiness;
    private bool readinessObserved;
    private int disposed;

    internal ReplicaFileReadinessFixture(ReplicaCrashBoundary boundary = ReplicaCrashBoundary.TermSaved)
    {
        try
        {
            stores = new(boundary);
        }
        catch (Exception)
        {
            cancellation.Dispose();
            throw;
        }
    }

    internal string Root => stores.Root;

    internal void HoldMetadataWal(string store) => stores.HoldTargetFile(store, ReplicaFileReadinessStores.MetadataWal);

    internal void HoldSourceFile(string store, string file) => stores.HoldSourceFile(store, file);

    internal Task StartReadiness(ReplicaCrashBoundary boundary = ReplicaCrashBoundary.TermSaved)
        => readiness = ReplicaProcessFiles.WaitForOwnershipAsync(Root, cancellation.Token, boundary);

    internal async Task ObserveReadinessAsync()
    {
        var current = readiness ?? throw new InvalidOperationException("Replica readiness was not started.");
        try
        {
            await current.WaitAsync(TimeSpan.FromSeconds(6));
        }
        finally
        {
            readinessObserved = current.IsCompleted;
        }
    }

    internal Task CancelAsync() => cancellation.CancelAsync();

    internal async Task RunAsync(Func<Task> scenario)
    {
        try
        {
            await scenario();
        }
        catch (Exception)
        {
            try
            {
                await DisposeAsync();
            }
            catch (IOException)
            {
            }
            catch (OperationCanceledException)
            {
            }
            catch (AggregateException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            throw;
        }
    }

    internal void ReleaseMetadataWal() => stores.ReleaseHeldFile();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await cancellation.CancelAsync();
        }
        finally
        {
            try
            {
                stores.ReleaseHeldFile();
            }
            finally
            {
                await CleanupReadinessAndRootAsync();
            }
        }
    }

    private async Task CleanupReadinessAndRootAsync()
    {
        try
        {
            await ObservePendingReadinessAsync();
        }
        finally
        {
            try
            {
                await stores.DeleteRootAsync(CancellationToken.None);
            }
            finally
            {
                await DisposeOwnedFieldsAsync();
            }
        }
    }

    private async ValueTask DisposeOwnedFieldsAsync()
    {
        try
        {
            await stores.DisposeAsync();
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task ObservePendingReadinessAsync()
    {
        if (readiness is null || readinessObserved)
        {
            return;
        }

        try
        {
            await readiness;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }
}
