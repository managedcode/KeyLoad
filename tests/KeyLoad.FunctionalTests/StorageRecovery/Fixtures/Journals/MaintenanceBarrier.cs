using ZoneTree;
using ZoneTree.Segments;
using StorageKey = ZoneTree.Transactional.Versioning.VersionedKey<System.Memory<byte>>;
using StorageValue = ZoneTree.Transactional.Versioning.VersionedValue<System.Memory<byte>>;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class MaintenanceBarrier : IDisposable
{
    internal const int DeadlineSeconds = 10;
    private readonly ManualResetEventSlim release = new();
    internal TaskCompletionSource<Thread> Created { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<MergeResult> Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Exception? Failure
    {
        get; private set;
    }

    internal void DiskCreated(IZoneTreeMaintenance<StorageKey, StorageValue> _,
        IDiskSegment<StorageKey, StorageValue> segment, bool bottom)
    {
        if (bottom || segment.Length == 0)
        {
            Failure = new InvalidOperationException("The original execution merge produced an unexpected segment.");
        }
        Created.TrySetResult(Thread.CurrentThread);
        if (!release.Wait(TimeSpan.FromSeconds(DeadlineSeconds)))
        {
            Failure = new TimeoutException("The original execution maintenance callback did not settle.");
        }
    }

    internal void MergeEnded(IZoneTreeMaintenance<StorageKey, StorageValue> _, MergeResult result)
        => Ended.TrySetResult(result);

    internal void Release() => release.Set();
    public void Dispose() => release.Dispose();
}
