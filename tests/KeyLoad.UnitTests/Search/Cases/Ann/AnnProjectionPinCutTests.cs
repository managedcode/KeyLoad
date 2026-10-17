using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnProjectionPinCutTests
{
    private const string ConsumerName = "ann-pin-cut-v1";
    private static readonly ImmutableArray<float> BeforeSeedValues = [11.25f, -2.5f, 0.75f];
    private static readonly ImmutableArray<float> AfterSeedValues = [-0f, 7.125f, 3.5f];

    [Test]
    public async Task PinPrecedesSeedAndEmptySignedBridgeStopsExactlyAtSeedTail()
    {
        using var database = AnnProjectionPinTestSupport.Create();
        var consumer = AnnProjectionPinTestSupport.Consumer(database, ConsumerName);
        var observedTail = AnnProjectionPinTestSupport.Tail(database);
        var pin = AnnProjectionPinTestSupport.Configure(database, consumer, observedTail);
        await AnnProjectionPinCutAssertions.AssertPinAsync(database, consumer, observedTail, pin);

        AnnProjectionPinTestSupport.PutVector(database, BeforeSeedValues);
        var seed = AnnProjectionPinTestSupport.Capture(database);
        var seedTail = seed.Cut.OutboxTail;
        await AnnProjectionPinCutAssertions.AssertCapturedAsync(seed, observedTail,
            seedTail, BeforeSeedValues);

        AnnProjectionPinTestSupport.PutVector(database, AfterSeedValues);
        var beforeBridge = AnnProjectionPinTestSupport.CaptureBridge(database, consumer, seedTail);
        var bridge = AnnProjectionPinTestSupport.Read(database, consumer, limit: 1);
        await AnnProjectionPinCutAssertions.AssertBridgeAsync(bridge, observedTail,
            seedTail, BeforeSeedValues);

        var commandId = Guid.NewGuid();
        var evaluatedAt = TimeProvider.System.GetUtcNow();
        var applied = AnnProjectionPinTestSupport.CommitEmpty(database, consumer, bridge,
            AnnProjectionPinTestSupport.Principal, commandId, evaluatedAt).Get<ProjectionBatchResult>();
        var afterApplyPosition = database.Store.Position;
        var replayed = AnnProjectionPinTestSupport.CommitEmpty(database, consumer, bridge,
            AnnProjectionPinTestSupport.Principal, commandId, evaluatedAt).Get<ProjectionBatchResult>();
        await AnnProjectionPinCutAssertions.AssertReplayAsync(database, applied, replayed,
            seedTail, afterApplyPosition);
        await AnnProjectionPinCutAssertions.AssertNoSourceEffectsAsync(database, consumer,
            beforeBridge, seedTail);
        await AnnProjectionPinCutAssertions.AssertLaterVectorAsync(database, consumer,
            seedTail, AfterSeedValues);
    }
}
