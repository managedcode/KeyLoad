using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnProjectionPinRetentionTests
{
    private const string ConsumerName = "ann-pin-retention-v1";
    private static readonly ImmutableArray<float> CapturedValues = [1.25f, 2.5f, 5f];
    private static readonly ImmutableArray<float> LaterValues = [9.25f, 8.5f, 7f];

    [Test]
    public async Task ActivePinBlocksPurgeThenReleasesOnlyItsOwnRetainedPrefix()
    {
        using var database = AnnProjectionPinTestSupport.Create();
        var consumer = AnnProjectionPinTestSupport.Consumer(database, ConsumerName);
        var start = AnnProjectionPinTestSupport.Tail(database);
        AnnProjectionPinTestSupport.Configure(database, consumer, start);
        AnnProjectionPinTestSupport.PutVector(database, CapturedValues);
        var seed = AnnProjectionPinTestSupport.Capture(database);
        var seedTail = seed.Cut.OutboxTail;
        AnnProjectionPinTestSupport.PutVector(database, LaterValues);
        var sourceTail = seedTail + 1;
        var before = database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition);
        var retained = AnnProjectionPinTestSupport.CaptureEntryBytes(database, 1, sourceTail);

        await AnnProjectionPinRetentionAssertions.AssertPurgeBlockedAsync(database,
            consumer, start, sourceTail, before, retained);
        var bridge = AnnProjectionPinTestSupport.Read(database, consumer, limit: 1);
        await AnnProjectionPinRetentionAssertions.AssertBridgeAsync(bridge, seedTail, CapturedValues);
        var committed = AnnProjectionPinTestSupport.CommitEmpty(database, consumer, bridge,
            AnnProjectionPinTestSupport.Principal, Guid.NewGuid(), TimeProvider.System.GetUtcNow()).Get<ProjectionBatchResult>();
        await Assert.That(committed.Checkpoint).IsEqualTo(seedTail);

        var purged = AnnProjectionPinTestSupport.Purge(database, seedTail).Get<OutboxHead>();
        await AnnProjectionPinRetentionAssertions.AssertPurgedThroughSeedAsync(purged,
            seedTail, sourceTail);
        var later = AnnProjectionPinTestSupport.Read(database, consumer, limit: 1);
        await AnnProjectionPinRetentionAssertions.AssertLaterEntryAsync(later,
            sourceTail, LaterValues);

        var released = AnnProjectionPinTestSupport.Release(database, consumer);
        await Assert.That(released.Error).IsNull();
        await AnnProjectionPinRetentionAssertions.AssertReleasedTokenAsync(database,
            consumer, later, seedTail);
        await AnnProjectionPinRetentionAssertions.AssertReleasedIdentityCannotBeReusedAsync(
            database, consumer, sourceTail);
        var reclaimed = AnnProjectionPinTestSupport.Purge(database, sourceTail).Get<OutboxHead>();
        await AnnProjectionPinRetentionAssertions.AssertReclaimedAsync(database,
            consumer, reclaimed, sourceTail);
    }
}
