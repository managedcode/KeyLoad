using KeyLoad.UnitTests.Features.ClusterRouting;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestOperations;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

/// <summary>Complete canonical bytes and original native cuts, independent from disposable derived directories.</summary>
internal sealed record AnalyticsStorePolicyPublicationCut(string[] Image, long Position, long Applied,
    long LastIndex, long CommittedIndex, long Term, OutboxStatus Outbox)
{
    internal static AnalyticsStorePolicyPublicationCut Capture(TestDatabase db)
    {
        var outbox = db.Database.GetOutboxStatus(D.Root, db.Partition);
        var journal = db.ReadJournalCut();
        return new(ControlledPartitionMovementRawImage.Bytes(db.Store), db.Store.Position, db.Database.LastApplied,
            journal.LastIndex, journal.CommittedIndex, journal.Term, outbox);
    }

    internal async Task AssertUnchangedAsync(TestDatabase db)
    {
        var journal = db.ReadJournalCut();
        await Assert.That(db.Store.Position).IsEqualTo(Position);
        await Assert.That(db.Database.LastApplied).IsEqualTo(Applied);
        await Assert.That(journal.LastIndex).IsEqualTo(LastIndex);
        await Assert.That(journal.CommittedIndex).IsEqualTo(CommittedIndex);
        await Assert.That(journal.Term).IsEqualTo(Term);
        await O.EqualAsync(Outbox, db.Database.GetOutboxStatus(D.Root, db.Partition));
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(db.Store)
            .SequenceEqual(Image, StringComparer.Ordinal)).IsTrue();
    }
}
