namespace KeyLoad.FunctionalTests.Search;

internal sealed class AnnProjectionPinBudgetTests
{
    private const string CorruptHistoryConsumer = "ann-pin-corrupt-history";

    [Test]
    public async Task UnavailableStartAndMalformedOrMissingHistoryFailWithoutCheckpointAdvance()
    {
        using var database = AnnProjectionPinTestSupport.Create();
        var originalHead = database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Head;
        var purge = AnnProjectionPinTestSupport.Purge(database, 1).Get<OutboxHead>();
        await Assert.That(purge.FirstAvailable).IsEqualTo(2L);
        var consumer = AnnProjectionPinTestSupport.Consumer(database, CorruptHistoryConsumer);
        var checkpoint = purge.FirstAvailable - 1;
        AnnProjectionPinTestSupport.Configure(database, consumer, checkpoint);
        await AnnProjectionPinHistoryAssertions.AssertUnavailableStartAsync(database, purge);
        var entry = AnnProjectionPinHistoryAssertions.CorruptSequence(database, consumer,
            purge.FirstAvailable);
        await AnnProjectionPinHistoryAssertions.AssertCorruptReadUnchangedAsync(database,
            consumer, checkpoint, purge.FirstAvailable, entry);
        AnnProjectionPinHistoryAssertions.Restore(database, purge.FirstAvailable, entry.Original);
        await AnnProjectionPinHistoryAssertions.AssertGapReadUnchangedAsync(database,
            consumer, checkpoint, purge.FirstAvailable, entry.ConsumerBytes);
        AnnProjectionPinHistoryAssertions.Restore(database, purge.FirstAvailable, entry.Original);
        await Assert.That(database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Head.Tail)
            .IsEqualTo(originalHead.Tail);
    }
}
