using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class NativeMaintenanceJoinFlow
{
    private const string RootPrefix = "keyload-maintenance-join-";
    private const string RetainedRoot = "KeyLoad.NativeMaintenance.RetainedRoot";

    internal static async Task RunAsync(CancellationToken testToken)
    {
        var root = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString("N"));
        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        Task? disposal = null;
        using var barrier = new NativeMaintenanceBarrier();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(NativeMaintenanceBarrier.DeadlineSeconds), TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, testToken);
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var options = new ZoneTreeStoreOptions(root).ResolveExecutionOptions(UnitExecutionOptions.StorageExecution());
                var runtime = new ZoneTreeStoreRuntime(options);
                store = new ZoneTreeStore(runtime, runtime.Identity.NodeId);
                Seed(store);
                await NativeMaintenanceState.LiteralAsync(store, healthy: false);
                var image = NativeMaintenanceState.Image(store);
                var position = store.Position;
                var identity = store.Identity.Incarnation;
                runtime.Tree.Maintenance.OnDiskSegmentCreated += barrier.DiskCreated;
                runtime.Tree.Maintenance.OnMergeOperationEnded += barrier.MergeEnded;
                runtime.Maintainer.ThresholdForMergeOperationStart = int.MaxValue;
                runtime.Maintainer.MaximumReadOnlySegmentCount = int.MaxValue;
                runtime.Maintainer.EvictToDisk();
                var merger = await barrier.Created.Task.WaitAsync(caller.Token);
                store.Read(_ =>
                {
                    disposal = Task.Run(store.Dispose, CancellationToken.None);
                    if (!SpinWait.SpinUntil(() => runtime.Gate.WaitingWriteCount > 0,
                        TimeSpan.FromSeconds(NativeMaintenanceBarrier.DeadlineSeconds)))
                    { throw new TimeoutException("The original owner disposal did not reach its native writer gate."); }
                    return true;
                });
                await ObserveAcquiredWriterAsync(runtime, disposal!, caller.Token);
                await Assert.That(disposal!.IsCompleted).IsFalse();
                barrier.Release();
                await disposal.WaitAsync(caller.Token);
                await Assert.That(await barrier.Ended.Task.WaitAsync(caller.Token)).IsEqualTo(MergeResult.SUCCESS);
                await Assert.That(merger.ThreadState).IsEqualTo(ThreadState.Stopped);
                if (barrier.Failure is { } observerFailure)
                { throw observerFailure; }
                await VerifyReopenAsync(root, image, position, identity);
            }, failures);
        }
        finally
        {
            barrier.Release();
            if (disposal is not null)
            {
                await ServerFailureObserver.ObserveAsync(async () => await disposal, failures);
            }
            if (store is not null)
            { ServerFailureObserver.Observe(store.Dispose, failures); }
            if (barrier.Failure is { } failure && !failures.Contains(failure))
            { failures.Add(failure); }
            if (failures.Count == 0)
            { ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures); }
            foreach (var retainedFailure in failures)
            { retainedFailure.Data[RetainedRoot] = root; }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyReopenAsync(string root, string[] image, long position, Guid identity)
    {
        NativeMaintenanceState.ExclusiveFiles(root);
        await VerifyOpeningAsync(root, image, position, identity, healthy: false);
        NativeMaintenanceState.ExclusiveFiles(root);
        await VerifyOpeningAsync(root, image, position + 1, identity, healthy: true);
    }

    private static async Task VerifyOpeningAsync(string root, string[] image, long position, Guid identity, bool healthy)
    {
        ZoneTreeStore? reopened = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                reopened = Open(root);
                await Assert.That(reopened.Position).IsEqualTo(position);
                await Assert.That(reopened.Identity.Incarnation).IsEqualTo(identity);
                await NativeMaintenanceState.LiteralAsync(reopened, healthy);
                if (!healthy)
                {
                    await Assert.That(NativeMaintenanceState.Image(reopened)).IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
                    reopened.Commit((tx, _) => { tx.Put(NativeMaintenanceState.Key("omega"), NativeMaintenanceState.Value("healthy")); return true; });
                    await NativeMaintenanceState.LiteralAsync(reopened, healthy: true);
                }
            }, failures);
        }
        finally
        {
            if (reopened is not null)
            { ServerFailureObserver.Observe(reopened.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void Seed(ZoneTreeStore store)
    {
        store.Commit((tx, _) =>
        {
            foreach (var key in new[] { "alpha", "beta", "gamma" })
            { tx.Put(NativeMaintenanceState.Key(key), NativeMaintenanceState.Value("original")); }
            return true;
        });
        store.Commit((tx, _) =>
        {
            tx.Put(NativeMaintenanceState.Key("alpha"), NativeMaintenanceState.Value("updated"));
            tx.Delete(NativeMaintenanceState.Key("beta"));
            return true;
        });
    }

    private static async Task ObserveAcquiredWriterAsync(ZoneTreeStoreRuntime runtime, Task disposal, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (disposal.IsCompleted)
            { await disposal; throw new InvalidOperationException("Owner disposal completed before the held native merge settled."); }
            if (!runtime.Gate.TryEnterReadLock(0))
            {
                if (runtime.Gate.WaitingWriteCount == 0)
                { return; }
                await Task.Yield();
                continue;
            }
            runtime.Gate.ExitReadLock();
            await Task.Yield();
        }
    }

    private static ZoneTreeStore Open(string root) => new(new(root), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
}
