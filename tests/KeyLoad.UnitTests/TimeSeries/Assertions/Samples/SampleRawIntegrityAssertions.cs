using System.Text.Json;
using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleRawIntegrityAssertions
{
    private const int MaximumImageRecords = 4096;
    private const int OneMutation = 1;
    private const string ImageBound = "The complete time-series fixture image exceeds its bound.";
    private const string ExpiryKind = "expireSamples";

    internal static SampleRawIntegrityCut Capture(ZoneTreeStore store)
        => new(store.Position, OrleansSerialization.Serialize(store.Identity), store.Read(view =>
        {
            var page = view.Scan([], MaximumImageRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException(ImageBound);
            }
            return JsonSerializer.Serialize(page.Records.Select(record =>
                new[] { Convert.ToHexString(record.Key.Span), Convert.ToHexString(record.Value.Span) }).ToArray());
        }));

    internal static async Task UnchangedAsync(ZoneTreeStore store, SampleRawIntegrityCut cut)
    {
        var actual = Capture(store);
        await Assert.That(actual.Position).IsEqualTo(cut.Position);
        await Assert.That(actual.Identity.SequenceEqual(cut.Identity)).IsTrue();
        await Assert.That(actual.Image).IsEqualTo(cut.Image);
    }

    internal static async Task RefuseAsync(SampleRawIntegrityFixture fixture, Command<DatabaseCommand> expiry)
    {
        await RefuseOneAsync(fixture, () => SampleRawIntegrityScenario.ReadRange(fixture, final: false));
        await RefuseOneAsync(fixture, () => fixture.Owner.ReadLatestSample(SampleAggregateTestData.RootPrincipal,
            SampleRawIntegrityScenario.Latest(fixture, final: false)));
        await RefuseOneAsync(fixture, () => fixture.Owner.AggregateSamples(SampleAggregateTestData.RootPrincipal,
            SampleRawIntegrityScenario.Aggregate(fixture, final: false)));
        await RefuseOneAsync(fixture, () => fixture.Owner.AggregateSampleWindows(SampleAggregateTestData.RootPrincipal,
            SampleRawIntegrityScenario.Windows(fixture, final: false)));
        await RefuseOneAsync(fixture, () => fixture.Apply(expiry));
    }

    private static async Task RefuseOneAsync(SampleRawIntegrityFixture fixture, Action operation)
    {
        var cut = Capture(fixture.Store);
        var failure = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await UnchangedAsync(fixture.Store, cut);
    }

    internal static async Task ReplayAsync(SampleRawIntegrityFixture fixture, Command<DatabaseCommand> operation,
        CommitReceipt original)
    {
        var cut = Capture(fixture.Store);
        var replay = fixture.Apply(operation);
        await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
        await UnchangedAsync(fixture.Store, cut);
    }

    internal static async Task ExpiryReceiptAsync(SampleRawIntegrityFixture fixture, CommitReceipt receipt,
        Command<DatabaseCommand> operation, long expectedApplied)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(operation.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(fixture.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsEqualTo(expectedApplied);
        await Assert.That(fixture.Owner.LastApplied).IsEqualTo(expectedApplied);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(OneMutation);
        var expected = new MutationReceipt(ExpiryKind, SampleAggregateTestData.Set, SampleAggregateTestData.Series, 3);
        await Assert.That(OrleansSerialization.Serialize(receipt.Mutations[0]).SequenceEqual(
            OrleansSerialization.Serialize(expected))).IsTrue();
    }

    internal static async Task HealthyAsync(SampleRawIntegrityFixture fixture, bool final)
    {
        var cut = Capture(fixture.Store);
        var actualRows = SampleRawIntegrityScenario.ReadRange(fixture, final);
        var expectedRows = final
            ? new[] { SampleRawIntegrityScenario.Record(4), SampleRawIntegrityScenario.Record(5) }
            : new[] { SampleRawIntegrityScenario.Record(2), SampleRawIntegrityScenario.Record(3) };
        await Assert.That(actualRows.Length).IsEqualTo(expectedRows.Length);
        for (var index = 0; index < expectedRows.Length; index++)
        {
            await Assert.That(OrleansSerialization.Serialize(actualRows[index]).SequenceEqual(
                OrleansSerialization.Serialize(expectedRows[index]))).IsTrue();
        }
        var latest = fixture.Owner.ReadLatestSample(SampleAggregateTestData.RootPrincipal,
            SampleRawIntegrityScenario.Latest(fixture, final));
        await Assert.That(latest.Sample).IsEqualTo(expectedRows[1]);
        var expectedAggregate = final ? new SampleAggregate(2, 18, 8, 10, 9) : new(2, 10, 4, 6, 5);
        var aggregate = fixture.Owner.AggregateSamples(SampleAggregateTestData.RootPrincipal,
            SampleRawIntegrityScenario.Aggregate(fixture, final));
        await Assert.That(aggregate).IsEqualTo(expectedAggregate);
        var windows = fixture.Owner.AggregateSampleWindows(SampleAggregateTestData.RootPrincipal,
            SampleRawIntegrityScenario.Windows(fixture, final));
        await Assert.That(windows.Windows.Length).IsEqualTo(1);
        await Assert.That(windows.Windows[0]).IsEqualTo(new SampleAggregateWindow(
            final ? SampleRawIntegrityScenario.FinalFloor : SampleRawIntegrityScenario.InitialFloor,
            final ? SampleRawIntegrityScenario.FinalUntil : SampleRawIntegrityScenario.InitialUntil, expectedAggregate));
        await RequireAuthorityAsync(fixture, final);
        await UnchangedAsync(fixture.Store, cut);
    }

    private static async Task RequireAuthorityAsync(SampleRawIntegrityFixture fixture, bool final)
    {
        var status = fixture.Owner.ReadSampleRetention(SampleAggregateTestData.RootPrincipal,
            new(fixture.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series));
        await Assert.That(status).IsEqualTo(new SampleRetentionStatus(
            final ? SampleRawIntegrityScenario.FinalFloor : SampleRawIntegrityScenario.InitialFloor, final ? 3 : 1, false));
        var raw = fixture.Store.Read(view => view.Scan(
            SampleReadKeys.Prefix(fixture.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series), MaximumImageRecords));
        var expected = final
            ? new[] { SampleRawIntegrityScenario.Record(4), SampleRawIntegrityScenario.Record(5) }
            : new[] { SampleRawIntegrityScenario.Record(2), SampleRawIntegrityScenario.Record(3), SampleRawIntegrityScenario.Record(4) };
        await Assert.That(raw.HasMore).IsFalse();
        await Assert.That(raw.Records.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(raw.Records[index].Value.Span.SequenceEqual(OrleansSerialization.Serialize(expected[index]))).IsTrue();
        }
        var sequence = OrleansSerialization.Deserialize<long>(fixture.Store.Read(view => view.ReadOwnedValue(
            SampleRollupKeys.Sequence(fixture.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series)))!);
        await Assert.That(sequence).IsEqualTo(final ? 5L : 4L);
    }
}
