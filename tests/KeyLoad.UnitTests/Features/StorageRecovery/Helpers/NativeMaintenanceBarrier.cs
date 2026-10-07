using ZoneTree;
using ZoneTree.Segments;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativeMaintenanceBarrier : IDisposable
{
    internal const int DeadlineSeconds = 10;
    private readonly ManualResetEventSlim release = new();
    internal TaskCompletionSource<Thread> Created { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<MergeResult> Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Exception? Failure { get; private set; }

    internal void DiskCreated(IZoneTreeMaintenance<Memory<byte>, Memory<byte>> _,
        IDiskSegment<Memory<byte>, Memory<byte>> segment, bool bottom)
    {
        if (bottom || segment.Length == 0)
        {
            Failure = new InvalidOperationException("The original native merge produced an unexpected segment.");
        }
        Created.TrySetResult(Thread.CurrentThread);
        if (!release.Wait(TimeSpan.FromSeconds(DeadlineSeconds)))
        {
            Failure = new TimeoutException("The original native maintenance callback did not settle.");
        }
    }

    internal void MergeEnded(IZoneTreeMaintenance<Memory<byte>, Memory<byte>> _, MergeResult result)
        => Ended.TrySetResult(result);

    internal void Release() => release.Set();
    public void Dispose() => release.Dispose();
}
