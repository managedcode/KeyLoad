using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class RetirementConservationHeaderFlow
{
    internal static async Task RunAsync(CurrentStorageFixture files, StorageFailureOwnerLifetime ownership, int defect)
    {
        StoreIdentity? identity = null;
        await OwnedAsync(files, ownership, async store =>
        {
            store.Commit((tx, _) =>
            {
                tx.Put(files.FirstKey, files.FirstValue);
                tx.Put(files.SecondKey, files.SecondValue);
                return true;
            });
            identity = store.Identity;
            RetirementConservationHeaderImage.Write(files.InvalidSnapshot, store, files.SourceOptions, defect);
            var image = await File.ReadAllBytesAsync(files.InvalidSnapshot);
            var before = await CurrentStorageSnapshot.CaptureAsync(files.Source);
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => store.InstallSnapshot(files.InvalidSnapshot, 0));
            await Assert.That(failure.Code).IsEqualTo(defect == 4 ? ErrorCode.FormatUnsupported : ErrorCode.Corruption);
            await CurrentStorageSnapshot.AssertUnchangedAsync(files.Source, before);
            await Assert.That((await File.ReadAllBytesAsync(files.InvalidSnapshot)).SequenceEqual(image)).IsTrue();
            await Assert.That(store.Position).IsEqualTo(1L);
            await RequireAsync(files, store, identity, healthy: false);
            var next = store.Commit((tx, position) => { tx.Put(files.SecondKey, files.FirstValue); return position; });
            await Assert.That(next).IsEqualTo(2L);
            await RequireAsync(files, store, identity, healthy: true);
            identity = await SnapshotAsync(files, store, identity);
        });
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await OwnedAsync(files, ownership, store => RequireAsync(files, store, identity!, healthy: true));
        }
    }

    private static async Task<StoreIdentity> SnapshotAsync(CurrentStorageFixture files, ZoneTreeStore store,
        StoreIdentity original)
    {
        var expected = new StorageSnapshot(original.Incarnation, 2L, 0L, 2L);
        var created = store.CreateSnapshot(files.Snapshot, 0L);
        await Assert.That(created).IsEqualTo(expected);
        await Assert.That(store.VerifySnapshot(files.Snapshot)).IsEqualTo(expected);
        await Assert.That(store.InstallSnapshot(files.Snapshot, 0L)).IsEqualTo(expected);
        var installed = original with
        {
            ReadGeneration = checked(original.ReadGeneration + 1L)
        };
        await RequireAsync(files, store, installed, healthy: true);
        return installed;
    }

    internal static async Task OwnedAsync(CurrentStorageFixture files, StorageFailureOwnerLifetime ownership,
        Func<ZoneTreeStore, Task> body,
        Microsoft.Extensions.Options.IOptions<ZoneTreeStorageExecutionOptions>? execution = null)
    {
        var failures = new List<Exception>();
        var store = ownership.Acquire(() => new ZoneTreeStore(files.SourceOptions,
            execution ?? UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()), failures);
        if (store is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => body(store), failures);
            ownership.Close(store, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task RequireAsync(CurrentStorageFixture files, ZoneTreeStore store,
        StoreIdentity identity, bool healthy)
    {
        await WalFileFixture.AssertPreservedIdentity(identity, store.Identity);
        await Assert.That(store.Identity.MinimumReaderContract).IsEqualTo(identity.MinimumReaderContract);
        await Assert.That(store.Identity.DispatchPaused).IsEqualTo(identity.DispatchPaused);
        await Assert.That(store.Identity.ReadGeneration).IsEqualTo(identity.ReadGeneration);
        await Assert.That(store.Position).IsEqualTo(healthy ? 2L : 1L);
        var page = store.Read(view => view.Scan([], 3));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(2);
        await Assert.That(page.Records[0].Key.Span.SequenceEqual(files.FirstKey)).IsTrue();
        await Assert.That(page.Records[0].Value.Span.SequenceEqual(files.FirstValue)).IsTrue();
        await Assert.That(page.Records[1].Key.Span.SequenceEqual(files.SecondKey)).IsTrue();
        await Assert.That(page.Records[1].Value.Span.SequenceEqual(healthy ? files.FirstValue : files.SecondValue)).IsTrue();
    }
}
