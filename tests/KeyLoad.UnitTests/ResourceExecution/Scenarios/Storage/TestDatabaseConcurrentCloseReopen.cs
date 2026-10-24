using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.IO;
using KeyLoad.UnitTests.StorageRecovery;

namespace KeyLoad.UnitTests.ResourceExecution;

internal static class TestDatabaseConcurrentCloseReopen
{
    internal static async Task HeldAsync(TestDatabase fixture)
    {
        foreach (var directory in Directories(fixture))
        {
            var options = new ZoneTreeStoreOptions(directory).ResolveExecutionOptions(fixture.StorageExecution);
            var error = Assert.Throws<IOException>(() =>
            {
                using var unexpected = ZoneTreeOwnerLock.Open(options, requireExisting: true);
            });
            await Assert.That(error).IsNotNull();
            await Assert.That(OfflineErrors.IsLockContention(error!)).IsTrue();
        }
    }

    internal static async Task RunAsync(TestDatabase fixture, TestDatabaseConcurrentCloseSeed state)
    {
        await Assert.That(Directory.Exists(fixture.Directory)).IsTrue();
        var failures = new List<Exception>();
        var lifetime = new StorageFailureOwnerLifetime();
        foreach (var directory in Directories(fixture))
        {
            if (!lifetime.CanAdvance)
            {
                break;
            }
            var options = new ZoneTreeStoreOptions(directory).ResolveExecutionOptions(fixture.StorageExecution);
            var ownership = lifetime.Acquire(() => ZoneTreeOwnerLock.Open(options, requireExisting: true), failures);
            if (ownership is not null)
            {
                lifetime.Close(ownership, failures);
            }
        }
        if (lifetime.CanAdvance)
        {
            var store = lifetime.Acquire(() => ZoneTreeExistingStore.Open(new(fixture.Directory)
            {
                Incarnation = state.Identity.Incarnation
            }, state.Identity.NodeId, fixture.StorageExecution), failures);
            if (store is not null)
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var owner = fixture.CreateConfiguredDatabase(store);
                    await TestDatabaseConcurrentCloseAssertions.CurrentAsync(owner, store, state);
                    var replay = owner.Apply(state.Original);
                    await TestDatabaseConcurrentCloseAssertions.ReceiptAsync(replay, state.ExpectedReceipt);
                    await ReplayResultAssertions.Same<CommitReceipt>(replay, state.Result);
                    await TestDatabaseConcurrentCloseAssertions.CurrentAsync(owner, store, state);
                }, failures);
                lifetime.Close(store, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static string[] Directories(TestDatabase fixture)
        => [fixture.Directory];
}
