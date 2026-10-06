using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class RuntimeJournalReaderLifecycleTests
{
    private const long ExpectedFirstPosition = 1;
    private const long ExpectedSecondPosition = 2;

    [Test]
    public async Task AcOrl013ReaderMarkSurvivesReopenCompactionBackupRestoreAndFurtherWrites()
    {
        using var fixture = new RuntimeJournalReaderFixture();
        using (var store = fixture.Open(fixture.CanonicalPath))
        {
            RuntimeJournalReaderFixture.WriteRuntimeRecord(store, RuntimeJournalReaderFixture.RuntimeJournalKey, RuntimeJournalReaderFixture.RuntimeJournalValue);
            var beforeMark = store.Identity;
            await Assert.That(store.Position).IsEqualTo(ExpectedFirstPosition);
            await Assert.That(RuntimeJournalReaderFixture.Read(store, RuntimeJournalReaderFixture.RuntimeJournalKey))
                .IsEquivalentTo(RuntimeJournalReaderFixture.RuntimeJournalValue, CollectionOrdering.Matching);

            store.RequireReaderContract(StoreReaderContract.RuntimeJournal);
            await AssertIdentityPreservedAsync(beforeMark, store.Identity, StoreReaderContract.RuntimeJournal);
            await Assert.That(store.Position).IsEqualTo(ExpectedFirstPosition);
            store.RequireReaderContract(StoreReaderContract.RuntimeJournal);
            await Assert.That(RuntimeJournalReaderFixture.Read(store, RuntimeJournalReaderFixture.RuntimeJournalKey))
                .IsEquivalentTo(RuntimeJournalReaderFixture.RuntimeJournalValue, CollectionOrdering.Matching);
        }

        using (var reopened = fixture.Open(fixture.CanonicalPath))
        {
            await Assert.That(reopened.Identity.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
            await Assert.That(RuntimeJournalReaderFixture.Read(reopened, RuntimeJournalReaderFixture.RuntimeJournalKey))
                .IsEquivalentTo(RuntimeJournalReaderFixture.RuntimeJournalValue, CollectionOrdering.Matching);
            RuntimeJournalReaderFixture.WriteRuntimeRecord(reopened, RuntimeJournalReaderFixture.FollowupKey, RuntimeJournalReaderFixture.FollowupValue);
            await Assert.That(reopened.Position).IsEqualTo(ExpectedSecondPosition);
            await Assert.That(RuntimeJournalReaderFixture.Read(reopened, RuntimeJournalReaderFixture.FollowupKey))
                .IsEquivalentTo(RuntimeJournalReaderFixture.FollowupValue, CollectionOrdering.Matching);
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
    public async Task AcOrl013PartialTwoStoreReaderMarkCanResumeIdempotentlyWithoutChangingData()
    {
        using var fixture = new RuntimeJournalReaderFixture();
        StoreIdentity canonicalBefore;
        StoreIdentity replicaBefore;
        using (var canonical = fixture.Open(fixture.CanonicalPath))
        using (var replica = fixture.Open(fixture.ReplicaPath))
        {
            RuntimeJournalReaderFixture.WriteRuntimeRecord(canonical, RuntimeJournalReaderFixture.CanonicalDocumentKey, RuntimeJournalReaderFixture.CanonicalDocumentValue);
            RuntimeJournalReaderFixture.WriteRuntimeRecord(replica, RuntimeJournalReaderFixture.ReplicaDocumentKey, RuntimeJournalReaderFixture.ReplicaDocumentValue);
            canonicalBefore = canonical.Identity;
            replicaBefore = replica.Identity;
            var canonicalPosition = canonical.Position;
            var replicaPosition = replica.Position;

            canonical.RequireReaderContract(StoreReaderContract.RuntimeJournal);
            await Assert.That(canonical.Identity.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
            await Assert.That(replica.Identity.MinimumReaderContract).IsEqualTo(StoreReaderContract.Legacy);
            await Assert.That(canonical.Position).IsEqualTo(canonicalPosition);
            await Assert.That(replica.Position).IsEqualTo(replicaPosition);
            await Assert.That(RuntimeJournalReaderFixture.Read(canonical, RuntimeJournalReaderFixture.CanonicalDocumentKey))
                .IsEquivalentTo(RuntimeJournalReaderFixture.CanonicalDocumentValue, CollectionOrdering.Matching);
            await Assert.That(RuntimeJournalReaderFixture.Read(replica, RuntimeJournalReaderFixture.ReplicaDocumentKey))
                .IsEquivalentTo(RuntimeJournalReaderFixture.ReplicaDocumentValue, CollectionOrdering.Matching);

            var firstMark = canonical.Identity;
            canonical.RequireReaderContract(StoreReaderContract.RuntimeJournal);
            await Assert.That(canonical.Identity).IsEqualTo(firstMark);
            replica.RequireReaderContract(StoreReaderContract.RuntimeJournal);
            await Assert.That(replica.Identity.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
            await Assert.That(canonical.Position).IsEqualTo(canonicalPosition);
            await Assert.That(replica.Position).IsEqualTo(replicaPosition);
        }

        using var reopenedCanonical = fixture.Open(fixture.CanonicalPath);
        using var reopenedReplica = fixture.Open(fixture.ReplicaPath);
        await AssertIdentityPreservedAsync(canonicalBefore, reopenedCanonical.Identity, StoreReaderContract.RuntimeJournal);
        await AssertIdentityPreservedAsync(replicaBefore, reopenedReplica.Identity, StoreReaderContract.RuntimeJournal);
        await Assert.That(RuntimeJournalReaderFixture.Read(reopenedCanonical, RuntimeJournalReaderFixture.CanonicalDocumentKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.CanonicalDocumentValue, CollectionOrdering.Matching);
        await Assert.That(RuntimeJournalReaderFixture.Read(reopenedReplica, RuntimeJournalReaderFixture.ReplicaDocumentKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.ReplicaDocumentValue, CollectionOrdering.Matching);
        RuntimeJournalReaderFixture.WriteRuntimeRecord(reopenedCanonical, RuntimeJournalReaderFixture.PartialCanonicalKey, RuntimeJournalReaderFixture.PartialCanonicalValue);
        RuntimeJournalReaderFixture.WriteRuntimeRecord(reopenedReplica, RuntimeJournalReaderFixture.PartialReplicaKey, RuntimeJournalReaderFixture.PartialReplicaValue);
        await Assert.That(RuntimeJournalReaderFixture.Read(reopenedCanonical, RuntimeJournalReaderFixture.PartialCanonicalKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.PartialCanonicalValue, CollectionOrdering.Matching);
        await Assert.That(RuntimeJournalReaderFixture.Read(reopenedReplica, RuntimeJournalReaderFixture.PartialReplicaKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.PartialReplicaValue, CollectionOrdering.Matching);
    }

    private static async Task AssertIdentityPreservedAsync(StoreIdentity expected, StoreIdentity actual, int readerContract)
    {
        await Assert.That(actual.FormatVersion).IsEqualTo(expected.FormatVersion);
        await Assert.That(actual.KeyCodecVersion).IsEqualTo(expected.KeyCodecVersion);
        await Assert.That(actual.NodeId).IsEqualTo(expected.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.SigningKey.Span.SequenceEqual(expected.SigningKey.Span)).IsTrue();
        await Assert.That(actual.Durability).IsEqualTo(expected.Durability);
        await Assert.That(actual.DispatchPaused).IsEqualTo(expected.DispatchPaused);
        await Assert.That(actual.ReadGeneration).IsEqualTo(expected.ReadGeneration);
        await Assert.That(actual.MinimumReaderContract).IsEqualTo(readerContract);
    }
}
