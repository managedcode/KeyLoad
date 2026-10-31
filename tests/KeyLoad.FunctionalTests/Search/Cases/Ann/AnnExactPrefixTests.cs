using System.Collections.Immutable;
using System.Text.Json;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class AnnExactPrefixTests
{
    private const string ConsumerName = "ann-exact-prefix";
    private const string UpperDetail = "The projection upper sequence is outside the current consumer checkpoint and retained head.";
    private const string ThroughField = "throughSequence";
    private const long Step = 1;
    private const int SingleEntry = 1;
    private const int BatchLimit = 10;
    private const int SnapshotBound = 128;
    private static readonly ImmutableArray<float> PrefixValues = [11.25f, -2.5f, 0.75f];
    private static readonly ImmutableArray<float> LaterValues = [-0f, 7.125f, 3.5f];

    [Test]
    public async Task ExactPrefixExcludesLaterWritesAndSignedCheckpointReplayRemainsHealthy()
    {
        using var db = AnnProjectionPinTestSupport.Create();
        var consumer = AnnProjectionPinTestSupport.Consumer(db, ConsumerName);
        var start = AnnProjectionPinTestSupport.Tail(db);
        AnnProjectionPinTestSupport.Configure(db, consumer, start);
        AnnProjectionPinTestSupport.PutVector(db, PrefixValues);
        var seed = AnnProjectionPinTestSupport.Capture(db);
        AnnProjectionPinTestSupport.PutVector(db, LaterValues);
        var request = new ReadProjectionBatchRequest(consumer, BatchLimit, seed.Cut.OutboxTail);
        var execution = OrleansSerialization.Deserialize<ReadProjectionBatchRequest>(OrleansSerialization.Serialize(request));
        var json = JsonSerializer.Deserialize<ReadProjectionBatchRequest>(JsonSerializer.Serialize(request, JsonDefaults.Options), JsonDefaults.Options)!;
        var before = Snapshot(db);
        var position = db.Store.Position;
        var batch = db.Database.ReadProjectionBatch(AnnProjectionPinTestSupport.Principal, execution);
        var jsonBatch = db.Database.ReadProjectionBatch(AnnProjectionPinTestSupport.Principal, json);
        await AssertPrefixAsync(batch, seed.Cut.OutboxTail);
        await AssertPrefixAsync(jsonBatch, seed.Cut.OutboxTail);
        await Assert.That(Snapshot(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        var commandId = Guid.NewGuid();
        var now = db.Database.EvaluationClock.GetUtcNow();
        var applied = AnnProjectionPinTestSupport.CommitEmpty(db, consumer, batch,
            AnnProjectionPinTestSupport.Principal, commandId, now).Get<ProjectionBatchResult>();
        var committed = Snapshot(db);
        var committedPosition = db.Store.Position;
        var replay = AnnProjectionPinTestSupport.CommitEmpty(db, consumer, batch,
            AnnProjectionPinTestSupport.Principal, commandId, now).Get<ProjectionBatchResult>();
        await Assert.That(OrleansSerialization.Serialize(replay)).IsEquivalentTo(OrleansSerialization.Serialize(applied), CollectionOrdering.Matching);
        await Assert.That(Snapshot(db)).IsEquivalentTo(committed, CollectionOrdering.Matching);
        await Assert.That(db.Store.Position).IsEqualTo(committedPosition);
        var empty = db.Database.ReadProjectionBatch(AnnProjectionPinTestSupport.Principal, request);
        await Assert.That(empty.Entries).IsEmpty();
        await Assert.That(empty.ThroughSequence).IsEqualTo(seed.Cut.OutboxTail);
        await Assert.That(empty.HasMore).IsFalse();
        await AnnProjectionPinCutAssertions.AssertLaterVectorAsync(db, consumer, seed.Cut.OutboxTail, LaterValues);
    }

    [Test]
    public async Task InvalidExactPrefixesHaveNoEffectsAndOrdinaryNullReadRemainsLatestHead()
    {
        using var db = AnnProjectionPinTestSupport.Create();
        var consumer = AnnProjectionPinTestSupport.Consumer(db, ConsumerName);
        var checkpoint = AnnProjectionPinTestSupport.Tail(db);
        AnnProjectionPinTestSupport.Configure(db, consumer, checkpoint);
        AnnProjectionPinTestSupport.PutVector(db, PrefixValues);
        var tail = AnnProjectionPinTestSupport.Tail(db);
        var before = Snapshot(db);
        var position = db.Store.Position;
        foreach (var upper in new[] { checkpoint - Step, tail + Step })
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.ReadProjectionBatch(
                AnnProjectionPinTestSupport.Principal, new(consumer, BatchLimit, upper)));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.TokenInvalidated);
            await Assert.That(error.Message).IsEqualTo(UpperDetail);
            await Assert.That(Snapshot(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
            await Assert.That(db.Store.Position).IsEqualTo(position);
            await AssertPrefixAsync(AnnProjectionPinTestSupport.Read(db, consumer, limit: BatchLimit), tail);
        }
        var ordinary = new ReadProjectionBatchRequest(consumer, BatchLimit);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ordinary, JsonDefaults.Options));
        await Assert.That(json.RootElement.TryGetProperty(ThroughField, out _)).IsFalse();
        var released = AnnProjectionPinTestSupport.Release(db, consumer).Get<ProjectionConsumerInfo>();
        await Assert.That(released.Released).IsTrue();
        var stale = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.ReadProjectionBatch(
            AnnProjectionPinTestSupport.Principal, new(consumer, BatchLimit, tail)));
        await Assert.That(stale.Code).IsEqualTo(ErrorCode.TokenInvalidated);
    }

    private static async Task AssertPrefixAsync(ProjectionBatch batch, long upper)
    {
        await Assert.That(batch.Entries.Length).IsEqualTo(SingleEntry);
        await Assert.That(batch.Entries[0].Sequence).IsEqualTo(upper);
        await Assert.That(batch.ThroughSequence).IsEqualTo(upper);
        await Assert.That(batch.HasMore).IsFalse();
        var mutation = (PutVector)batch.Entries[0].Mutation;
        await Assert.That(mutation.Values).IsEquivalentTo(PrefixValues, CollectionOrdering.Matching);
        await Assert.That(mutation.ExpectedDocumentRevision).IsEqualTo(Step);
    }

    private static (string Key, string Value)[] Snapshot(TestDatabase db) => db.Store.Read(view =>
    {
        var page = view.Scan([], SnapshotBound);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The exact projection prefix fixture exceeds its complete snapshot bound.");
        }
        return page.Records.Select(row => (Convert.ToHexString(row.Key.Span), Convert.ToHexString(row.Value.Span))).ToArray();
    });
}
