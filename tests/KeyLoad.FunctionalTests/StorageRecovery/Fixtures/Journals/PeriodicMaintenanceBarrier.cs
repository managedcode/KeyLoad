using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class PeriodicMaintenanceBarrier : IDisposable
{
    private readonly ManualResetEventSlim release = new();
    private readonly Exception? failure;

    internal PeriodicMaintenanceBarrier(Exception? failure = null) => this.failure = failure;
    internal global::Orleans.Runtime.IGrainContext OwnerContext { get; set; } = null!;
    internal global::Orleans.Runtime.IGrainContext? Context { get; private set; }
    internal TaskCompletionSource<ZoneTreeMaintenanceSweep> Swept { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Sweep(ZoneTreeMaintenanceSweep result)
    {
        Context = OwnerContext.ActivationServices.GetRequiredService<global::Orleans.Runtime.IGrainContextAccessor>().GrainContext;
        Swept.TrySetResult(result);
        if (!release.Wait(TimeSpan.FromSeconds(MaintenanceBarrier.DeadlineSeconds)))
        {
            throw new TimeoutException("The original post-execution cleanup callback did not settle.");
        }
        if (failure is not null)
        {
            throw failure;
        }
    }

    internal void Release() => release.Set();
    public void Dispose() => release.Dispose();
}
