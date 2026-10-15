using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class TransactionalReadViewState : IDisposable
{
    internal const int MaximumRows = 4_096;
    internal const long MaximumBytes = 524_288;
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    internal TestDatabase Harness { get; } = new();
    private readonly List<ZoneTreeStore> coldStores = [];
    internal ZoneTreeStore Store => coldStores.Count == 0 ? Harness.Store : coldStores[^1];
    internal DatabaseEngine Database => coldStores.Count == 0 ? Harness.Database : DocumentIndexCommittedScanOperations.CreateEngine(Store);
    internal string[] OldImage { get; set; } = [];
    internal string[] FinalImage { get; set; } = [];
    internal StoreIdentity Identity { get; set; } = null!;
    internal long OldPosition
    {
        get; set;
    }
    internal long FinalPosition
    {
        get; set;
    }
    internal ReplicatedOperation Writer { get; set; } = null!;
    internal CommitReceipt WriterReceipt { get; set; } = null!;
    internal ReplicatedOperation Healthy { get; set; } = null!;
    internal CommitReceipt HealthyReceipt { get; set; } = null!;
    internal byte[] WriterOutcome { get; set; } = [];
    internal byte[] HealthyOutcome { get; set; } = [];
    internal ZoneTreeReadCutLease Capture(CancellationToken token = default)
        => Store.Read(view => Store.CaptureNativeReadCut(view, new(MaximumRows, MaximumBytes, TimeSpan.FromMinutes(1)), token));
    internal void Reopen()
    {
        Store.Dispose();
        coldStores.Add(new(new(Harness.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()));
    }
    public void Dispose()
    {
        var failures = new List<Exception>();
        foreach (var store in coldStores)
        {
            ServerFailureObserver.Observe(store.Dispose, failures);
        }
        ServerFailureObserver.Observe(Harness.Store.Dispose, failures);
        if (failures.Count == 0)
        {
            ServerFailureObserver.Observe(Harness.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
