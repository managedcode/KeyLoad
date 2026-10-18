using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.UnitTests.Features.Authorization;
using KeyLoad.UnitTests.Features.InternalSerialization;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;
using N = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsBoundsNative;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestOperations;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsNativeMapColdReads
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
            foreach (var fault in Enum.GetValues<NativeImmutableDictionaryFault>())
            {
                var malformed = NativeImmutableDictionaryFixture.Encode(fault);
                await NativeWireCollectionTests.Reject(() => NativeSerialization.Deserialize<object>(malformed));
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
        var payload = NativeSerialization.Serialize(new GrainValue(actual));
        NativeSerialization.Validate(payload);
        var decoded = NativeSerialization.Deserialize<GrainValue>(payload).Value;
        await Assert.That(decoded).IsTypeOf<T>();
        await O.EqualAsync(expected, (T)decoded!);
        // The public gateway serializes the complete runtime typed value.
        var json = JsonSerializer.SerializeToUtf8Bytes(decoded, decoded!.GetType(), JsonDefaults.Options);
        await O.EqualAsync(expected, JsonDefaults.Deserialize<T>(json));
    }

    internal static async Task UnchangedAsync(SafeProjectorOperationDatabase owner, Func<Task> read)
    {
        var identity = NativeSerialization.Serialize(owner.Store.Identity);
        var image = TeamRowPolicyAssertions.Image(owner.Store);
        var position = owner.Store.Position;
        var applied = owner.Database.LastApplied;
        var journal = owner.ReadCanonicalCut();
        var outbox = owner.Database.GetOutboxStatus(D.Root, owner.Source.Partition);
        await read();
        await Assert.That(NativeSerialization.Serialize(owner.Store.Identity).SequenceEqual(identity)).IsTrue();
        await Assert.That(TeamRowPolicyAssertions.Image(owner.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(owner.Store.Position).IsEqualTo(position);
        await Assert.That(owner.Database.LastApplied).IsEqualTo(applied);
        await Assert.That(owner.ReadCanonicalCut()).IsEqualTo(journal);
        await O.EqualAsync(outbox, owner.Database.GetOutboxStatus(D.Root, owner.Source.Partition));
    }
}
