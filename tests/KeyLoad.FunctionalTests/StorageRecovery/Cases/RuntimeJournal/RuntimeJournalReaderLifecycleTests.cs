using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class RuntimeJournalReaderLifecycleTests
{
    private const long ExpectedFirstPosition = 1;
    private const long ExpectedSecondPosition = 2;

    [Test]
    public async Task Ac001BornCurrentStoreReplaysBacksUpRestoresAndContinues()
    {
        using var fixture = new RuntimeJournalReaderFixture();
        using (var store = fixture.Open(fixture.CanonicalPath))
        {
            RuntimeJournalReaderFixture.WriteRuntimeRecord(store, RuntimeJournalReaderFixture.RuntimeJournalKey,
                RuntimeJournalReaderFixture.RuntimeJournalValue);
            await Assert.That(store.Position).IsEqualTo(ExpectedFirstPosition);
        }

        using (var reopened = fixture.Open(fixture.CanonicalPath))
        {
            await Assert.That(RuntimeJournalReaderFixture.Read(reopened, RuntimeJournalReaderFixture.RuntimeJournalKey))
                .IsEquivalentTo(RuntimeJournalReaderFixture.RuntimeJournalValue, CollectionOrdering.Matching);
            RuntimeJournalReaderFixture.WriteRuntimeRecord(reopened, RuntimeJournalReaderFixture.FollowupKey,
                RuntimeJournalReaderFixture.FollowupValue);
            await Assert.That(reopened.Position).IsEqualTo(ExpectedSecondPosition);
        }

        using var cold = fixture.Open(fixture.CanonicalPath);
        await Assert.That(RuntimeJournalReaderFixture.Read(cold, RuntimeJournalReaderFixture.RuntimeJournalKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.RuntimeJournalValue, CollectionOrdering.Matching);
        await Assert.That(RuntimeJournalReaderFixture.Read(cold, RuntimeJournalReaderFixture.FollowupKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.FollowupValue, CollectionOrdering.Matching);
        await RuntimeJournalReaderLifecycleAssertions.AssertBackupRestoreAsync(cold, fixture);
    }
}
