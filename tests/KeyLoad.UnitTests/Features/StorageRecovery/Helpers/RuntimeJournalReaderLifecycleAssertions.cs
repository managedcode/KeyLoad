using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class RuntimeJournalReaderLifecycleAssertions
{
    internal static async Task AssertBackupRestoreAsync(ZoneTreeStore compacted, RuntimeJournalReaderFixture fixture)
    {
        var backupPosition = compacted.CreateBackup(fixture.BackupPath);
        var verified = ZoneTreeStore.VerifyBackup(fixture.BackupPath, UnitExecutionOptions.StorageExecution());
        await Assert.That(verified.Position).IsEqualTo(backupPosition);
        await Assert.That(verified.Identity.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);

        var restoredIdentity = ZoneTreeStore.Restore(fixture.BackupPath, fixture.RestorePath,
            UnitExecutionOptions.StorageExecution());
        await Assert.That(restoredIdentity.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
        using var restored = fixture.Open(fixture.RestorePath, restoredIdentity.Incarnation);
        await Assert.That(restored.Identity.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
        await Assert.That(RuntimeJournalReaderFixture.Read(restored, RuntimeJournalReaderFixture.RuntimeJournalKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.RuntimeJournalValue, CollectionOrdering.Matching);
        await Assert.That(RuntimeJournalReaderFixture.Read(restored, RuntimeJournalReaderFixture.FollowupKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.FollowupValue, CollectionOrdering.Matching);
        RuntimeJournalReaderFixture.WriteRuntimeRecord(restored, RuntimeJournalReaderFixture.FollowupKey,
            RuntimeJournalReaderFixture.RuntimeJournalValue);
        await Assert.That(RuntimeJournalReaderFixture.Read(restored, RuntimeJournalReaderFixture.FollowupKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.RuntimeJournalValue, CollectionOrdering.Matching);
    }
}
