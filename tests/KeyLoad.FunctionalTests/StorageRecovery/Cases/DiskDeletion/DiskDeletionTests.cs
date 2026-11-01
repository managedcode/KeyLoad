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
        var options = new ZoneTreeStoreOptions(files.DirectoryPath);
        byte[] deleted = [0x10];
        byte[] retained = [0x20];
        byte[] healthy = [0x30];
        byte[] value = [0x00, 0xFF, 0x80];
        StoreIdentity identity;
        using (var store = new ZoneTreeStore(options, UnitExecutionOptions.StorageExecution()))
        {
            store.Commit((tx, _) => { tx.Put(deleted, value); tx.Put(retained, value); return true; });
            PersistToDisk(store);
            store.Commit((tx, _) => { tx.Delete(deleted); return true; });
            await Assert.That(store.Position).IsEqualTo(2L);
            identity = store.Identity;
        }
        using (var reopened = new ZoneTreeStore(options, UnitExecutionOptions.StorageExecution()))
        {
            await StoreFileFixture.AssertPreservedIdentity(identity, reopened.Identity);
            await Assert.That(reopened.Position).IsEqualTo(2L);
            await Assert.That(reopened.Read(view => view.ReadOwnedValue(deleted))).IsNull();
            await Assert.That(reopened.Read(view => view.ReadOwnedValue(retained))!.SequenceEqual(value)).IsTrue();
            reopened.Commit((tx, _) => { tx.Put(healthy, []); return true; });
        }
        using var final = new ZoneTreeStore(options, UnitExecutionOptions.StorageExecution());
        await StoreFileFixture.AssertPreservedIdentity(identity, final.Identity);
        await Assert.That(final.Position).IsEqualTo(3L);
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
