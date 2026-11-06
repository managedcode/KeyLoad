using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class RuntimeJournalReaderLifecycleTests
{

    [Test]
    public async Task Ac001BornCurrentStoreReplaysBacksUpRestoresAndContinues()
    {
        using var fixture = new RuntimeJournalReaderFixture();
        long position = 0;
        using (var store = fixture.Open(fixture.CanonicalPath))
        {
            RuntimeJournalReaderFixture.WriteRuntimeRecord(store, RuntimeJournalReaderFixture.RuntimeJournalKey,
                RuntimeJournalReaderFixture.RuntimeJournalValue);
            position = await NativeStoreAssertions.AdvancedAsync(store, position);
        }

        using (var reopened = fixture.Open(fixture.CanonicalPath))
        {
            position = await NativeStoreAssertions.ReopenedAsync(reopened, position);
            await Assert.That(RuntimeJournalReaderFixture.Read(reopened, RuntimeJournalReaderFixture.RuntimeJournalKey))
                .IsEquivalentTo(RuntimeJournalReaderFixture.RuntimeJournalValue, CollectionOrdering.Matching);
            RuntimeJournalReaderFixture.WriteRuntimeRecord(reopened, RuntimeJournalReaderFixture.FollowupKey,
                RuntimeJournalReaderFixture.FollowupValue);
            position = await NativeStoreAssertions.AdvancedAsync(reopened, position);
        }

        using var cold = fixture.Open(fixture.CanonicalPath);
        await NativeStoreAssertions.ReopenedAsync(cold, position);
        await Assert.That(RuntimeJournalReaderFixture.Read(cold, RuntimeJournalReaderFixture.RuntimeJournalKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.RuntimeJournalValue, CollectionOrdering.Matching);
        await Assert.That(RuntimeJournalReaderFixture.Read(cold, RuntimeJournalReaderFixture.FollowupKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.FollowupValue, CollectionOrdering.Matching);
        await RuntimeJournalReaderLifecycleAssertions.AssertBackupRestoreAsync(cold, fixture);
    }
}
