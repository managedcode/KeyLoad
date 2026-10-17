using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class NativePeriodicMaintenanceFlow
{
    private const string RootPrefix = "keyload-periodic-maintenance-";
    private const string RetainedRoot = "KeyLoad.NativePeriodicMaintenance.RetainedRoot";
    private const int SweepIntervalMilliseconds = 50;

    internal static async Task RunAsync(bool failSweep, CancellationToken testToken)
    {
        var root = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString("N"));
        var failures = new List<Exception>();
        var injected = failSweep ? new InvalidOperationException("Owned post-native cleanup fault.") : null;
        ZoneTreeStore? store = null;
        Task? disposal = null;
        var originalFailureVerified = false;
        using var disk = new NativeMaintenanceBarrier();
        using var periodic = new NativePeriodicMaintenanceBarrier(injected);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(NativeMaintenanceBarrier.DeadlineSeconds), TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, testToken);
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var runtime = new ZoneTreeStoreRuntime(new ZoneTreeStoreOptions(root).ResolveExecutionOptions(UnitExecutionOptions.StorageExecution()));
                store = new(runtime, runtime.Identity.NodeId);
                await PrepareDiskAsync(runtime, store, disk, caller.Token);
                var image = NativeMaintenanceState.Image(store);
                var position = store.Position;
                var identity = store.Identity.Incarnation;
                ZoneTreeStoreHandleDisposal.RetireMaintainer(runtime);
                var execution = UnitExecutionOptions.StorageExecution(new()
                {
                    NativeMaintenanceInterval = TimeSpan.FromMilliseconds(SweepIntervalMilliseconds),
                    NativeBlockCacheLifetime = TimeSpan.Zero
                });
                runtime.Maintenance = new(runtime.Tree, execution, TimeProvider.System, periodic.Sweep);
                var maintenance = runtime.Maintenance;
                var originalWorker = maintenance.Worker;
                var actualSweep = await periodic.Swept.Task.WaitAsync(caller.Token);
                await Assert.That(actualSweep.ReadBuffers).IsGreaterThanOrEqualTo(0);
                await Assert.That(actualSweep.CacheKeys).IsGreaterThanOrEqualTo(0);
                await Assert.That(actualSweep.CacheValues).IsGreaterThanOrEqualTo(0);
                StartDisposalUnderRead(runtime, store, started => disposal = started);
                await NativeMaintenanceJoinFlow.ObserveAcquiredWriterAsync(runtime, disposal!, caller.Token);
                await Assert.That(disposal!.IsCompleted).IsFalse();
                await Assert.That(originalWorker.IsCompleted).IsFalse();
                periodic.Release();
                await JoinOriginalAsync(disposal!, originalWorker, injected, caller.Token);
                originalFailureVerified = injected is not null;
                await VerifyRepeatedDisposalAsync(maintenance.Dispose, injected);
                await NativeMaintenanceJoinFlow.VerifyReopenAsync(root, image, position, identity);
            }, failures);
        }
        finally
        {
            disk.Release();
            periodic.Release();
            await SettleDisposalAsync(disposal, injected, originalFailureVerified, failures);
            await SettleStoreAsync(store, injected, originalFailureVerified, failures);
            if (failures.Count == 0)
            {
                ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
            }
            foreach (var failure in failures)
            {
                failure.Data[RetainedRoot] = root;
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task SettleStoreAsync(ZoneTreeStore? store, Exception? expected,
        bool originalFailureVerified, List<Exception> failures)
    {
        if (store is null)
        {
            return;
        }
        if (originalFailureVerified)
        {
            await ServerFailureObserver.ObserveAsync(() => VerifyRepeatedDisposalAsync(store.Dispose, expected), failures);
        }
        else
        {
            ServerFailureObserver.Observe(store.Dispose, failures);
        }
    }

    private static async Task PrepareDiskAsync(ZoneTreeStoreRuntime runtime, ZoneTreeStore store,
        NativeMaintenanceBarrier disk, CancellationToken token)
    {
        NativeMaintenanceJoinFlow.Seed(store);
        runtime.Tree.Maintenance.OnDiskSegmentCreated += disk.DiskCreated;
        runtime.Tree.Maintenance.OnMergeOperationEnded += disk.MergeEnded;
        runtime.Maintainer.ThresholdForMergeOperationStart = int.MaxValue;
        runtime.Maintainer.MaximumReadOnlySegmentCount = int.MaxValue;
        runtime.Maintainer.EvictToDisk();
        var merger = await disk.Created.Task.WaitAsync(token);
        disk.Release();
        await Assert.That(await disk.Ended.Task.WaitAsync(token)).IsEqualTo(MergeResult.SUCCESS);
        await runtime.Maintainer.WaitForBackgroundThreadsAsync().WaitAsync(token);
        merger.Join();
        if (disk.Failure is { } failure)
        {
            throw failure;
        }
        await NativeMaintenanceState.LiteralAsync(store, healthy: false);
    }

    private static void StartDisposalUnderRead(ZoneTreeStoreRuntime runtime, ZoneTreeStore store, Action<Task> started)
        => store.Read(_ =>
        {
            var disposal = Task.Run(store.Dispose, CancellationToken.None);
            started(disposal);
            if (!SpinWait.SpinUntil(() => runtime.Gate.WaitingWriteCount > 0,
                TimeSpan.FromSeconds(NativeMaintenanceBarrier.DeadlineSeconds)))
            {
                throw new TimeoutException("The original cleanup owner did not reach its native writer gate.");
            }
            return disposal;
        });

    private static async Task JoinOriginalAsync(Task disposal, Task worker, Exception? injected, CancellationToken token)
    {
        if (injected is null)
        {
            await disposal.WaitAsync(token);
            await worker.WaitAsync(token);
            return;
        }
        var disposalFailure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await disposal.WaitAsync(token));
        var workerFailure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await worker.WaitAsync(token));
        await Assert.That(ReferenceEquals(disposalFailure, injected)).IsTrue();
        await Assert.That(ReferenceEquals(workerFailure, injected)).IsTrue();
    }

    private static async Task VerifyRepeatedDisposalAsync(Action dispose, Exception? expected)
    {
        if (expected is null)
        {
            dispose();
            return;
        }
        var failure = Assert.ThrowsExactly<InvalidOperationException>(dispose);
        await Assert.That(ReferenceEquals(failure, expected)).IsTrue();
    }

    private static async Task SettleDisposalAsync(Task? disposal, Exception? expected,
        bool originalFailureVerified, List<Exception> failures)
    {
        if (disposal is null)
        {
            return;
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (!originalFailureVerified)
            {
                await disposal;
                return;
            }
            var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await disposal);
            await Assert.That(ReferenceEquals(failure, expected)).IsTrue();
        }, failures);
    }
}
