using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using P = KeyLoad.UnitTests.Features.StorageRecovery.CheckpointOriginalCauseProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class CheckpointOriginalCauseFlow
{
    internal static async Task RunAsync(CurrentStorageFixture files, string mode, StorageFailureOwnerLifetime lifetime)
    {
        StoreIdentity identity = null!;
        await OwnedAsync(files, lifetime, async store =>
        {
            store.Commit((tx, _) =>
            {
                tx.Put(files.FirstKey, files.FirstValue);
                tx.Put(files.SecondKey, files.SecondValue);
                return true;
            });
            identity = store.Identity;
            await CheckpointOriginalCauseAssertions.CurrentAsync(files, store, identity);
            store.CreateSnapshot(files.Snapshot, P.Applied);
            var before = await File.ReadAllBytesAsync(Path.Combine(files.Source, P.Identity));
            var journal = await WalFileFixture.ReadJournalAsync(Path.Combine(files.Source, P.Journal));
            Directory.CreateDirectory(Path.Combine(files.Source, P.Temporary));
            await CheckpointOriginalCauseAssertions.FailureAsync(files, store, mode, before, journal);
        });
        Directory.Delete(Path.Combine(files.Source, P.Temporary));
        await OwnedAsync(files, lifetime, async store =>
        {
            await CheckpointOriginalCauseAssertions.CurrentAsync(files, store, identity);
            Execute(files, store, mode);
            identity = identity with
            {
                ReadGeneration = mode == P.Install ? checked(identity.ReadGeneration + 1) : identity.ReadGeneration
            };
            await CheckpointOriginalCauseAssertions.CurrentAsync(files, store, identity);
        });
        for (var cycle = 0; cycle < P.ColdCycles; cycle++)
        {
            await OwnedAsync(files, lifetime, store => CheckpointOriginalCauseAssertions.CurrentAsync(files, store, identity));
        }
    }

    internal static void Execute(CurrentStorageFixture files, ZoneTreeStore store, string mode)
    {
        if (mode == P.Compact)
        {
            store.Compact();
            return;
        }
        store.InstallSnapshot(files.Snapshot, P.Applied);
    }

    private static async Task OwnedAsync(CurrentStorageFixture files, StorageFailureOwnerLifetime lifetime, Func<ZoneTreeStore, Task> body)
    {
        var failures = new List<Exception>();
        var store = lifetime.Acquire(() => new ZoneTreeStore(files.SourceOptions,
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (store is not null)
            {
                await body(store);
            }
        }, failures);
        if (store is not null)
        {
            lifetime.Close(store, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
