using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.UnitTests.Authorization;
using KeyLoad.UnitTests.InternalSerialization;
using D = KeyLoad.UnitTests.AnalyticsStore.AnalyticsStoreTestData;
using N = KeyLoad.UnitTests.AnalyticsStore.RuntimeAnalyticsBounds;
using O = KeyLoad.UnitTests.AnalyticsStore.AnalyticsStoreTestOperations;

namespace KeyLoad.UnitTests.AnalyticsStore;

internal static class AnalyticsMapColdReads
{
    internal static async Task VerifyAsync(SafeProjectorOperationDatabase owner, AnalyticsEventData[] events, CancellationToken token)
    {
        await UnchangedAsync(owner, async () =>
        {
            var expected = new AnalyticsEventsPage(events.Select(row => new AnalyticsEventRecord(row.Id,
                row.Timestamp, row.Dimensions, row.Metrics, null, [], 1)).ToImmutableArray(), false, null);
            var page = owner.Database.ReadAnalyticsEvents(D.Root, D.Read(owner.Source), token);
            await PublicRoundTripAsync(expected, page);
            var aggregate = N.Query(owner.Database, D.Query(owner.Source), token);
            await PublicRoundTripAsync(new AnalyticsAggregateResult(
                [D.Row(D.Alpha, 2, 6, 3, 2, 4), D.Row(D.Beta, 1, 8, 8, 8, 8)], 3), aggregate);
        });
    }

    internal static async Task RefusalsAsync(SafeProjectorOperationDatabase owner, AnalyticsEventData[] events, CancellationToken token)
    {
        await UnchangedAsync(owner, async () =>
        {
            using var serializer = new SerializerFixture();
            var page = owner.Database.ReadAnalyticsEvents(D.Root, D.Read(owner.Source), token);
            var payload = OrleansSerialization.Serialize(new GrainValue(page));
            byte[][] malformed = [[], [0xff], serializer.EncodeNullRoot(), serializer.Encode(null),
                OrleansSerialization.Serialize(page), [.. payload, 0x01]];
            foreach (var bytes in malformed)
            {
                await SerializedPayloadAssertions.CorruptAsync(() => OrleansSerialization.Deserialize<GrainValue>(bytes));
                await VerifyAsync(owner, events, token);
            }
            AnalyticsEventsPage? published = null;
            var bounded = AnalyticsBoundsOwner.ReadEngine(owner.Source, new()
            {
                MaxQueryReadBytes = 1
            });
            var readFailure = Assert.ThrowsExactly<KeyLoadException>(() => published = bounded.ReadAnalyticsEvents(D.Root, D.Read(owner.Source), token));
            await Assert.That(readFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(published).IsNull();
            await VerifyAsync(owner, events, token);
            await N.RefusedAsync(() => N.Query(owner.Database, D.Query(owner.Source), token, resultBytes: 1));
            await VerifyAsync(owner, events, token);
            using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
            await cancelled.CancelAsync();
            var cancelledFailure = Assert.ThrowsExactly<OperationCanceledException>(() => published = owner.Database.ReadAnalyticsEvents(
                D.Root, D.Read(owner.Source), cancelled.Token));
            await Assert.That(cancelledFailure.CancellationToken).IsEqualTo(cancelled.Token);
            await Assert.That(published).IsNull();
            AnalyticsAggregateResult? cancelledAggregate = null;
            var aggregateFailure = Assert.ThrowsExactly<OperationCanceledException>(() =>
                cancelledAggregate = N.Query(owner.Database, D.Query(owner.Source), cancelled.Token));
            ArgumentNullException.ThrowIfNull(aggregateFailure);
            await Assert.That(aggregateFailure.CancellationToken).IsEqualTo(cancelled.Token);
            await Assert.That(cancelledAggregate).IsNull();
            token.ThrowIfCancellationRequested();
            await VerifyAsync(owner, events, token);
        });
    }

    private static async Task PublicRoundTripAsync<T>(T expected, T actual) where T : class
    {
        var payload = OrleansSerialization.Serialize(new GrainValue(actual));
        OrleansSerialization.Validate(payload);
        var decoded = OrleansSerialization.Deserialize<GrainValue>(payload).Value;
        await Assert.That(decoded).IsTypeOf<T>();
        await O.EqualAsync(expected, (T)decoded!);
        // The public gateway serializes the complete runtime typed value.
        var json = JsonSerializer.SerializeToUtf8Bytes(decoded, decoded!.GetType(), JsonDefaults.Options);
        await O.EqualAsync(expected, JsonDefaults.Deserialize<T>(json));
    }

    internal static async Task UnchangedAsync(SafeProjectorOperationDatabase owner, Func<Task> read)
    {
        var identity = OrleansSerialization.Serialize(owner.Store.Identity);
        var image = TeamRowPolicyAssertions.Image(owner.Store);
        var position = owner.Store.Position;
        var applied = owner.Database.LastApplied;
        var journal = owner.ReadCanonicalCut();
        var outbox = owner.Database.GetOutboxStatus(D.Root, owner.Source.Partition);
        await read();
        await Assert.That(OrleansSerialization.Serialize(owner.Store.Identity).SequenceEqual(identity)).IsTrue();
        await Assert.That(TeamRowPolicyAssertions.Image(owner.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(owner.Store.Position).IsEqualTo(position);
        await Assert.That(owner.Database.LastApplied).IsEqualTo(applied);
        await Assert.That(owner.ReadCanonicalCut()).IsEqualTo(journal);
        await O.EqualAsync(outbox, owner.Database.GetOutboxStatus(D.Root, owner.Source.Partition));
    }
}
