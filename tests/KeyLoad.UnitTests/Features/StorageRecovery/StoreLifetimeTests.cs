using System.Text;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class StoreLifetimeTests
{
    private const int CanonicalJournalHeaderBytes = 52;
    private const string GuidFormat = "N";
    private const string JournalFileName = "commands.wal";
    private const string OwnerLockFileName = "owner.lock";
    private const string TemporaryDirectoryPrefix = "keyload-store-lifetime-";
    private const string StoreKeyText = "lifetime/committed";
    private const string StoreValueText = "committed bytes survive reopen";
    private const string ReadFacadeMismatchMessage = "Read did not receive the public store facade.";

    [Test]
    public async Task AcSq001FacadeImplementsStoreAndViewAndReadReceivesTheSameFacade()
    {
        using var directory = new StoreDirectoryFixture();
        using var store = new ZoneTreeStore(new(directory.Path));
        var key = Encoding.UTF8.GetBytes(StoreKeyText);
        var expected = Encoding.UTF8.GetBytes(StoreValueText);

        var atomicStore = store.Read(static view => (IAtomicStore)view);
        var (actual, position) = CommitAndReadThroughAtomicStore(atomicStore, store, store, key, expected);

        await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(position).IsEqualTo(1);
    }

    [Test]
    public async Task AcSq002ExclusiveOwnerLockAndDoubleDisposeAllowIdentityPreservingReopen()
    {
        using var directory = new StoreDirectoryFixture();
        var key = Encoding.UTF8.GetBytes(StoreKeyText);
        var expected = Encoding.UTF8.GetBytes(StoreValueText);
        using var store = new ZoneTreeStore(new(directory.Path));
        var identity = store.Identity;
        store.Commit((transaction, _) =>
        {
            transaction.Put(key, expected);
            return true;
        });

        await Assert.That(File.Exists(Path.Combine(directory.Path, OwnerLockFileName))).IsTrue();
        var lockFailure = Assert.ThrowsExactly<IOException>(() =>
        {
            using var attempted = new ZoneTreeStore(new(directory.Path));
        });
        await Assert.That(lockFailure).IsNotNull();

        store.Dispose();
        store.Dispose();
        using var reopened = new ZoneTreeStore(new(directory.Path));
        await AssertIdentityMatches(identity, reopened.Identity);
        await Assert.That(reopened.Read(view => view.ReadOwnedValue(key)))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(reopened.Position).IsEqualTo(1);
    }

    [Test]
    public async Task AcSq002CorruptCompleteJournalHeaderReleasesOwnershipForRepairedReopen()
    {
        using var directory = new StoreDirectoryFixture();
        StoreIdentity originalIdentity;
        using (var initial = new ZoneTreeStore(new(directory.Path)))
        {
            originalIdentity = initial.Identity;
        }

        var journalPath = Path.Combine(directory.Path, JournalFileName);
        await File.WriteAllBytesAsync(journalPath, new byte[CanonicalJournalHeaderBytes]);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var attempted = new ZoneTreeStore(new(directory.Path));
        });
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);

        using (var releasedOwnership = new FileStream(Path.Combine(directory.Path, OwnerLockFileName),
                   FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
        }

        await Task.Run(() => TruncateJournalAndFlushToDisk(journalPath));

        using var reopened = new ZoneTreeStore(new(directory.Path));
        await AssertIdentityMatches(originalIdentity, reopened.Identity);
        var key = Encoding.UTF8.GetBytes(StoreKeyText);
        var expected = Encoding.UTF8.GetBytes(StoreValueText);
        reopened.Commit((transaction, _) =>
        {
            transaction.Put(key, expected);
            return true;
        });

        await Assert.That(reopened.Read(view => view.ReadOwnedValue(key)))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(reopened.Position).IsEqualTo(1);
    }

    private static async Task AssertIdentityMatches(StoreIdentity expected, StoreIdentity actual)
    {
        await Assert.That(actual.NodeId).IsEqualTo(expected.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.SigningKey.Span.SequenceEqual(expected.SigningKey.Span)).IsTrue();
    }

    private static (byte[]? Value, long Position) CommitAndReadThroughAtomicStore(IAtomicStore atomicStore,
        IKeyValueView viewFacade, ZoneTreeStore store, byte[] key, byte[] value)
    {
        atomicStore.Commit((transaction, _) =>
        {
            transaction.Put(key, value);
            return true;
        });

        var actual = atomicStore.Read(view =>
        {
            if (!ReferenceEquals(store, view) || !ReferenceEquals(viewFacade, view))
            {
                throw new InvalidOperationException(ReadFacadeMismatchMessage);
            }

            return view.ReadOwnedValue(key);
        });

        return (actual, atomicStore.Position);
    }

    private static void TruncateJournalAndFlushToDisk(string journalPath)
    {
        using var repairedJournal = new FileStream(journalPath, FileMode.Create, FileAccess.Write, FileShare.None);
        repairedJournal.Flush(true);
    }

    private sealed class StoreDirectoryFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            TemporaryDirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
