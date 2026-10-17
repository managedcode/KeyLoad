using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativePeriodicMaintenanceBarrier : IDisposable
{
    private readonly ManualResetEventSlim release = new();
    private readonly Exception? failure;

    internal NativePeriodicMaintenanceBarrier(Exception? failure = null) => this.failure = failure;
    internal TaskCompletionSource<ZoneTreeMaintenanceSweep> Swept { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Sweep(ZoneTreeMaintenanceSweep result)
    {
        Swept.TrySetResult(result);
        if (!release.Wait(TimeSpan.FromSeconds(NativeMaintenanceBarrier.DeadlineSeconds)))
        {
            throw new TimeoutException("The original post-native cleanup callback did not settle.");
        }
        if (failure is not null)
        {
            throw failure;
        }
    }

    internal void Release() => release.Set();
    public void Dispose() => release.Dispose();
}
