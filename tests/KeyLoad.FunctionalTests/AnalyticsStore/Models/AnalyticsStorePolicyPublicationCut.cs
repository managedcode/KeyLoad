using D = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestData;
using O = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestOperations;

namespace KeyLoad.FunctionalTests.AnalyticsStore;

/// <summary>Complete canonical bytes and original execution cuts, independent from disposable derived directories.</summary>
internal sealed record AnalyticsStorePolicyPublicationCut(string[] Image, long Position, long Applied,
    long AppliedPosition, long StorePosition, OutboxStatus Outbox)
{
    internal static AnalyticsStorePolicyPublicationCut Capture(TestDatabase db)
    {
        var outbox = db.Database.GetOutboxStatus(D.Root, db.Partition);
        var journal = db.ReadCanonicalCut();
        return new(CanonicalStoreImage.Bytes(db.Store), db.Store.Position, db.Database.LastApplied,
            journal.AppliedPosition, journal.StorePosition, outbox);
    }

    internal async Task AssertUnchangedAsync(TestDatabase db)
    {
        var journal = db.ReadCanonicalCut();
        await Assert.That(db.Store.Position).IsEqualTo(Position);
        await Assert.That(db.Database.LastApplied).IsEqualTo(Applied);
        await Assert.That(journal.AppliedPosition).IsEqualTo(AppliedPosition);
        await Assert.That(journal.StorePosition).IsEqualTo(StorePosition);
        await O.EqualAsync(Outbox, db.Database.GetOutboxStatus(D.Root, db.Partition));
        await Assert.That(CanonicalStoreImage.Bytes(db.Store)
            .SequenceEqual(Image, StringComparer.Ordinal)).IsTrue();
    }
}
