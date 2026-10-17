using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class RuntimeJournalReaderLifecycleTests
{
    private const long ExpectedFirstPosition = 1;
    private const long ExpectedSecondPosition = 2;

    [Test]
    public async Task AcNative001BornCurrentStoreReplaysCompactsBacksUpRestoresAndContinues()
    {
        using var fixture = new RuntimeJournalReaderFixture();
        using (var store = fixture.Open(fixture.CanonicalPath))
        {
            await Assert.That(store.Identity.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
            RuntimeJournalReaderFixture.WriteRuntimeRecord(store, RuntimeJournalReaderFixture.RuntimeJournalKey,
                RuntimeJournalReaderFixture.RuntimeJournalValue);
            await Assert.That(store.Position).IsEqualTo(ExpectedFirstPosition);
        }

        using (var reopened = fixture.Open(fixture.CanonicalPath))
        {
            await Assert.That(reopened.Identity.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
            await Assert.That(RuntimeJournalReaderFixture.Read(reopened, RuntimeJournalReaderFixture.RuntimeJournalKey))
                .IsEquivalentTo(RuntimeJournalReaderFixture.RuntimeJournalValue, CollectionOrdering.Matching);
            RuntimeJournalReaderFixture.WriteRuntimeRecord(reopened, RuntimeJournalReaderFixture.FollowupKey,
                RuntimeJournalReaderFixture.FollowupValue);
            await Assert.That(reopened.Position).IsEqualTo(ExpectedSecondPosition);
            reopened.Compact();
            await Assert.That(reopened.Identity.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
        }

        using var compacted = fixture.Open(fixture.CanonicalPath);
        await Assert.That(compacted.Identity.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
        await Assert.That(RuntimeJournalReaderFixture.Read(compacted, RuntimeJournalReaderFixture.RuntimeJournalKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.RuntimeJournalValue, CollectionOrdering.Matching);
        await Assert.That(RuntimeJournalReaderFixture.Read(compacted, RuntimeJournalReaderFixture.FollowupKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.FollowupValue, CollectionOrdering.Matching);
        await RuntimeJournalReaderLifecycleAssertions.AssertBackupRestoreAsync(compacted, fixture);
    }

    [Test]
    public async Task AcNative001CurrentSnapshotInstallPreservesCurrentCapabilityAndExactState()
    {
        using var fixture = new RuntimeJournalReaderFixture();
        using (var source = fixture.Open(fixture.CanonicalPath))
        {
            RuntimeJournalReaderFixture.WriteRuntimeRecord(source, RuntimeJournalReaderFixture.RuntimeJournalKey,
                RuntimeJournalReaderFixture.RuntimeJournalValue);
            source.CreateSnapshot(fixture.SnapshotPath, expectedAppliedPosition: 0);
        }

        using (var target = fixture.Open(fixture.ReplicaPath))
        {
            RuntimeJournalReaderFixture.WriteRuntimeRecord(target, RuntimeJournalReaderFixture.TargetDocumentKey,
                RuntimeJournalReaderFixture.TargetDocumentValue);
            await Assert.That(target.Identity.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
            var installed = target.InstallSnapshot(fixture.SnapshotPath, expectedAppliedPosition: 0);
            await Assert.That(installed.AppliedPosition).IsEqualTo(0L);
            await Assert.That(target.Identity.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
            await Assert.That(RuntimeJournalReaderFixture.Read(target, RuntimeJournalReaderFixture.RuntimeJournalKey))
                .IsEquivalentTo(RuntimeJournalReaderFixture.RuntimeJournalValue, CollectionOrdering.Matching);
            await Assert.That(RuntimeJournalReaderFixture.Read(target, RuntimeJournalReaderFixture.TargetDocumentKey)).IsNull();
        }

        using var reopened = fixture.Open(fixture.ReplicaPath);
        await Assert.That(reopened.Identity.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
        await Assert.That(RuntimeJournalReaderFixture.Read(reopened, RuntimeJournalReaderFixture.RuntimeJournalKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.RuntimeJournalValue, CollectionOrdering.Matching);
    }
}
