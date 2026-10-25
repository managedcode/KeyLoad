using System.Text.Json;
using KeyLoad.Core.TimeSeries;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class SampleMetadataIntegrityAssertions
{
    private const int MaximumRecords = 4096;
    internal static SampleMetadataIntegrityCut Capture(ZoneTreeStore store)
        => new(store.Position, OrleansSerialization.Serialize(store.Identity), store.Read(view =>
        {
            var page = view.Scan([], MaximumRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The complete metadata fixture image exceeds its bound.");
            }
            return JsonSerializer.Serialize(page.Records.Select(record =>
                new[] { Convert.ToHexString(record.Key.Span), Convert.ToHexString(record.Value.Span) }).ToArray());
        }));
    internal static async Task Unchanged(SampleMetadataIntegrityFixture fixture, SampleMetadataIntegrityCut cut)
    {
        var actual = Capture(fixture.Store);
        await Assert.That(actual.Position).IsEqualTo(cut.Position);
        await Assert.That(actual.Identity.SequenceEqual(cut.Identity)).IsTrue();
        await Assert.That(actual.Image).IsEqualTo(cut.Image);
    }
    internal static async Task Refuse(SampleMetadataIntegrityFixture fixture, SampleMetadataIntegrityFault fault,
        Command<DatabaseCommand> refresh, Command<DatabaseCommand> expiry)
    {
        var code = SampleMetadataIntegrityScenario.Code(fault);
        await Failure(fixture, () => SampleMetadataIntegrityScenario.Bucket(fixture), code);
        await Failure(fixture, () => fixture.Apply(refresh), code);
        if (!SampleMetadataIntegrityScenario.IsRetention(fault))
        {
            return;
        }
        await Failure(fixture, () => SampleMetadataIntegrityScenario.Range(fixture), code);
        await Failure(fixture, () => SampleMetadataIntegrityScenario.Latest(fixture), code);
        await Failure(fixture, () => SampleMetadataIntegrityScenario.Aggregate(fixture), code);
        await Failure(fixture, () => SampleMetadataIntegrityScenario.Windows(fixture), code);
        await Failure(fixture, () => SampleMetadataIntegrityScenario.Retention(fixture), code);
        await Failure(fixture, () => fixture.Apply(expiry), code);
    }
    internal static async Task Failure(SampleMetadataIntegrityFixture fixture, Action operation, ErrorCode code)
    {
        var cut = Capture(fixture.Store);
        var failure = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(failure.Code).IsEqualTo(code);
        await Unchanged(fixture, cut);
    }
    internal static async Task Replay(SampleMetadataIntegrityFixture fixture, Command<DatabaseCommand> operation,
        CommitReceipt receipt)
    {
        var cut = Capture(fixture.Store);
        var actual = fixture.Apply(operation);
        await Assert.That(OrleansSerialization.Serialize(actual).SequenceEqual(OrleansSerialization.Serialize(receipt))).IsTrue();
        await Unchanged(fixture, cut);
    }
    internal static async Task Healthy(SampleMetadataIntegrityFixture fixture, long revision, bool appended)
    {
        var cut = Capture(fixture.Store);
        var expectedRows = appended
            ? new[] { SampleMetadataIntegrityScenario.Record(2), SampleMetadataIntegrityScenario.Record(3),
                SampleMetadataIntegrityScenario.Record(4), SampleMetadataIntegrityScenario.Record(5) }
            : new[] { SampleMetadataIntegrityScenario.Record(2), SampleMetadataIntegrityScenario.Record(3),
                SampleMetadataIntegrityScenario.Record(4) };
        var expectedAggregate = appended ? new SampleAggregate(4, 28, 4, 10, 7) : new(3, 18, 4, 8, 6);
        var actualRows = SampleMetadataIntegrityScenario.Range(fixture);
        await Assert.That(actualRows.Length).IsEqualTo(expectedRows.Length);
        for (var index = 0; index < expectedRows.Length; index++)
        {
            await Assert.That(OrleansSerialization.Serialize(actualRows[index]).SequenceEqual(
                OrleansSerialization.Serialize(expectedRows[index]))).IsTrue();
        }
        await Assert.That(SampleMetadataIntegrityScenario.Latest(fixture).Sample).IsEqualTo(expectedRows[^1]);
        await Assert.That(SampleMetadataIntegrityScenario.Aggregate(fixture)).IsEqualTo(expectedAggregate);
        var windows = SampleMetadataIntegrityScenario.Windows(fixture);
        await Assert.That(windows.Windows.Length).IsEqualTo(1);
        await Assert.That(windows.Windows[0]).IsEqualTo(new SampleAggregateWindow(
            SampleMetadataIntegrityScenario.Floor, SampleMetadataIntegrityScenario.Until, expectedAggregate));
        await Assert.That(SampleMetadataIntegrityScenario.Retention(fixture)).IsEqualTo(
            new SampleRetentionStatus(SampleMetadataIntegrityScenario.Floor, 1, false));
        await Assert.That(SampleMetadataIntegrityScenario.Bucket(fixture)).IsEqualTo(new SampleRollupResult(revision,
            new(SampleMetadataIntegrityScenario.Floor, SampleMetadataIntegrityScenario.Until,
                appended ? 5 : 4, SampleMetadataIntegrityScenario.Floor, expectedAggregate)));
        var raw = fixture.Store.Read(view => view.Scan(SampleReadKeys.Prefix(fixture.Partition,
            SampleAggregateTestData.Set, SampleAggregateTestData.Series), MaximumRecords));
        await Assert.That(raw.HasMore).IsFalse();
        await Assert.That(raw.Records.Length).IsEqualTo(expectedRows.Length);
        for (var index = 0; index < expectedRows.Length; index++)
        {
            var expectedKey = KeySpace.Partition("sample", fixture.Partition, SampleAggregateTestData.Set,
                SampleAggregateTestData.Series, expectedRows[index].Sample.Timestamp, expectedRows[index].Sequence);
            await Assert.That(raw.Records[index].Key.Span.SequenceEqual(expectedKey)).IsTrue();
            await Assert.That(raw.Records[index].Value.Span.SequenceEqual(OrleansSerialization.Serialize(expectedRows[index]))).IsTrue();
        }
        var sequence = OrleansSerialization.Deserialize<long>(fixture.Store.Read(view => view.ReadOwnedValue(
            SampleRollupKeys.Sequence(fixture.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series)))!);
        await Assert.That(sequence).IsEqualTo(appended ? 5L : 4L);
        await Unchanged(fixture, cut);
    }
}
