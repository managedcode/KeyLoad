using KeyLoad.FunctionalTests.StorageRecovery;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests;

internal sealed class DiskDeletionTests
{
    [Test]
    public async Task DiskDeletionKeepsExactRowsAcrossTwoReopens()
    {
        using var files = new StoreFileFixture();
        var publications = new NativePublicationObservation();
        var options = new ZoneTreeStoreOptions(files.DirectoryPath) { FaultObserver = publications.Observe };
        long position = 0;
        byte[] deleted = [0x10];
        byte[] retained = [0x20];
        byte[] healthy = [0x30];
        byte[] value = [0x00, 0xFF, 0x80];
        StoreIdentity identity;
        using (var store = new ZoneTreeStore(options, UnitExecutionOptions.StorageExecution()))
        {
            store.Commit((tx, _) => { tx.Put(deleted, value); tx.Put(retained, value); return true; });
            PersistToDisk(store);
            var before = store.Position;
            store.Commit((tx, _) => { tx.Delete(deleted); return true; });
            position = await NativeStoreAssertions.AdvancedAsync(store, before);
            await Assert.That(publications.Count).IsEqualTo(2);
            identity = store.Identity;
        }
        using (var reopened = new ZoneTreeStore(options, UnitExecutionOptions.StorageExecution()))
        {
            await StoreFileFixture.AssertPreservedIdentity(identity, reopened.Identity);
            position = await NativeStoreAssertions.ReopenedAsync(reopened, position);
            await Assert.That(reopened.Read(view => view.ReadOwnedValue(deleted))).IsNull();
            await Assert.That(reopened.Read(view => view.ReadOwnedValue(retained))!.SequenceEqual(value)).IsTrue();
            reopened.Commit((tx, _) => { tx.Put(healthy, []); return true; });
            position = await NativeStoreAssertions.AdvancedAsync(reopened, position);
            await Assert.That(publications.Count).IsEqualTo(3);
        }
        using var final = new ZoneTreeStore(options, UnitExecutionOptions.StorageExecution());
        await StoreFileFixture.AssertPreservedIdentity(identity, final.Identity);
        await NativeStoreAssertions.ReopenedAsync(final, position);
        await Assert.That(publications.Count).IsEqualTo(3);
        await Assert.That(final.Read(view => view.ReadOwnedValue(deleted))).IsNull();
        await Assert.That(final.Read(view => view.ReadOwnedValue(retained))!.SequenceEqual(value)).IsTrue();
        await Assert.That(final.Read(view => view.ReadOwnedValue(healthy))).IsEmpty();
    }

    private static void PersistToDisk(ZoneTreeStore store)
    {
        store.Maintainer.EvictToDisk();
        store.Maintainer.WaitForBackgroundThreads();
    }
}
