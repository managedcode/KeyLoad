using KeyLoad.UnitTests.ResourceExecution;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using P = KeyLoad.UnitTests.TimeSeries.SampleChunkCanonicalFixture;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class SampleSequenceIntegrityAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task CounterAsync(SampleSequenceIntegrityFixture fixture, byte[] expected)
    {
        var actual = fixture.Store.Read(view => view.ReadOwnedValue(fixture.Counter));
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.AsSpan().SequenceEqual(expected)).IsTrue();
    }

    internal static async Task ReadRefusalAsync(SampleSequenceIntegrityFixture fixture, CancellationToken token)
    {
        var image = SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store);
        var cut = fixture.ReadCanonicalCut();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.ReadSampleChunkWindow(P.Principal,
            fixture.Source.Request, token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store)).IsEqualTo(image);
        await Assert.That(fixture.ReadCanonicalCut()).IsEqualTo(cut);
    }

    internal static async Task ReplayAsync(SampleSequenceIntegrityFixture fixture,
        Command<DatabaseCommand> original, Result<object> expected, byte[] outcomeBytes, CancellationToken token)
    {
        var image = SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store);
        var cut = fixture.ReadCanonicalCut();
        var actual = fixture.Database.ApplyEmbedded(original, token);
        await ReplayResultAssertions.Same<CommitReceipt>(actual, expected);
        await ReplayResultAssertions.Same<CommitReceipt>(fixture.Database.ResolveOutcome(original), expected);
        var retained = fixture.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.Key(original)));
        await Assert.That(retained).IsNotNull();
        await Assert.That(retained!.AsSpan().SequenceEqual(outcomeBytes)).IsTrue();
        await Assert.That(SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store)).IsEqualTo(image);
        await Assert.That(fixture.ReadCanonicalCut()).IsEqualTo(cut);
    }

    internal static async Task HealthyAsync(SampleSequenceIntegrityFixture fixture, bool merged, CancellationToken token)
    {
        var image = SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store);
        var position = fixture.Store.Position;
        SampleRecord[] rows = [new(P.Series, P.Initial[0], 1, P.Tags), new(P.Series, P.Initial[1], 2, P.Tags),
            new(P.Series, new("sequence-repaired", P.Start.AddTicks(1), -2d), 3, P.Tags),
            new(P.Series, new("sequence-fresh", P.Start.AddTicks(2), 6d), 4, P.Tags)];
        var aggregate = new SampleAggregate(4, 10d, -2d, 6d, 2.5d);
        await EqualAsync(rows, fixture.Database.ReadSamples(P.Principal, fixture.Source.Owner.Partition,
            P.Set, P.Series, P.Start, P.Until, P.OutputLimit, token));
        await EqualAsync(new LatestSampleResult(rows[3]), fixture.Database.ReadLatestSample(P.Principal,
            new(fixture.Source.Owner.Partition, P.Set, P.Series), token));
        await EqualAsync(aggregate, fixture.Database.AggregateSamples(P.Principal,
            new(fixture.Source.Owner.Partition, P.Set, P.Series, P.Start, P.Until), token));
        await EqualAsync(new SampleAggregateWindowsResult([new(P.Start, P.Until, aggregate)]),
            fixture.Database.AggregateSampleWindows(P.Principal,
                new(fixture.Source.Owner.Partition, P.Set, P.Series, P.Start, P.Until, P.Until - P.Start), token));
        await EqualAsync(new SampleChunkWindowResult(fixture.Source.WindowId, P.Start, P.Until,
            merged ? 2 : 1, merged ? 7 : 6, 4, null, [.. rows], position),
            fixture.Database.ReadSampleChunkWindow(P.Principal, fixture.Source.Request, token));
        await CounterAsync(fixture, OrleansSerialization.Serialize(4L));
        await OutboxAsync(fixture);
        await Assert.That(SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store)).IsEqualTo(image);
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
    }

    private static async Task OutboxAsync(SampleSequenceIntegrityFixture fixture)
    {
        var originals = fixture.Originals.ToArray();
        var prefix = KeySpace.Partition("outbox", fixture.Source.Owner.Partition);
        var page = fixture.Store.Read(view => view.Scan(prefix, fixture.Database.Limits.MaxScanRecords));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(originals.Length);
        for (var index = 0; index < originals.Length; index++)
        {
            var original = originals[index];
            var expected = new OutboxEntry(index + 1L, 0, original.Expected.Token,
                new DateTimeOffset(original.Operation.Timestamp), original.Command.Mutations[0], original.Expected.Mutations[0]);
            var actual = OrleansSerialization.Deserialize<OutboxEntry>(page.Records[index].Value.Span);
            await EqualAsync(expected, actual);
            await Assert.That(actual.Commit).IsEqualTo(expected.Commit);
            await Assert.That(actual.Mutation.GetType()).IsEqualTo(expected.Mutation.GetType());
            await Assert.That(actual.Receipt.CompositionReferences.IsEmpty).IsTrue();
        }
        var headKey = KeySpace.Partition("outbox-head", fixture.Source.Owner.Partition);
        var headBytes = fixture.Store.Read(view => view.ReadOwnedValue(headKey));
        await Assert.That(headBytes).IsNotNull();
        var expectedHead = new OutboxHead(originals.Length, 1L, originals.Length,
            page.Records.Sum(row => (long)row.Value.Length));
        await EqualAsync(expectedHead, OrleansSerialization.Deserialize<OutboxHead>(headBytes!));
        await EqualAsync(new OutboxStatus(expectedHead, []), fixture.Database.GetOutboxStatus(P.Principal,
            fixture.Source.Owner.Partition));
    }

}
