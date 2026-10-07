using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleRollupWholeFlow
{
    internal static readonly DateTimeOffset Start = SampleAggregateTestData.Start;
    internal static readonly DateTimeOffset End = Start.AddMinutes(1);
    internal const string Set = SampleAggregateTestData.Set;
    internal const string Series = SampleAggregateTestData.Series;
    internal const string Root = SampleAggregateTestData.RootPrincipal;
    internal const string StateBound = "The complete canonical state exceeds the fixture bound.";
    private const int MaximumRecords = 4096;

    internal static string Outcome(OperationResult result) => Convert.ToHexString(NativeSerialization.Serialize(result));

    internal static OperationResult Commit(TestDatabase db, Guid id, Mutation mutation, string principal = Root)
        => db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition, [mutation]), principal, id);
    internal static RefreshSampleRollup Refresh(long revision, int max = TimeSeriesReadDefaults.MaxSamples)
        => new(Set, Series, Start, End, revision, max);
    internal static SampleRollupResult Read(TestDatabase db, string principal = Root)
        => db.Database.ReadSampleRollup(principal, new(db.Partition, Set, Series, Start, End));
    internal static string Image(TestDatabase db, byte[]? prefix = null)
        => db.Store.Read(view =>
        {
            var scan = view.Scan(prefix ?? [], MaximumRecords);
            if (scan.HasMore)
            { throw new InvalidOperationException(StateBound); }
            return JsonSerializer.Serialize(scan.Records.Select(record =>
                new[] { Convert.ToHexString(record.Key.Span), Convert.ToHexString(record.Value.Span) }).ToArray());
        });
    internal static string Rollups(TestDatabase db)
        => Image(db, SampleRollupKeys.Prefix(db.Partition, Set, Series));
    internal static string Raw(TestDatabase db)
        => string.Join("|", Image(db, SampleReadKeys.Prefix(db.Partition, Set, Series)),
            Image(db, KeySpace.Partition("sample-id", db.Partition, Set, Series)),
            Image(db, SampleRollupKeys.Sequence(db.Partition, Set, Series)),
            Image(db, SampleRetentionStateReader.Key(db.Partition, Set, Series)));
    internal static void Seed(TestDatabase db)
    {
        SampleAggregateTestData.Configure(db);
        SampleAggregateTestData.Append(db,
            new("before", Start.AddTicks(-1), 100), new("a", Start, 2),
            new("b", Start.AddSeconds(30), 4), new("c", Start.AddSeconds(30).ToOffset(TimeSpan.FromHours(-5)), 6),
            new("boundary", End, 8), new("after", End.AddMinutes(1), 16));
    }
    internal static async Task Literal(SampleRollupResult actual, long revision, long sequence,
        long count, double sum, double? min, double? max, double? average)
    {
        await Assert.That(actual).IsEqualTo(new SampleRollupResult(revision,
            new(Start, End, sequence, null, new(count, sum, min, max, average))));
    }
    internal static async Task FailedRetry(TestDatabase db, RefreshSampleRollup mutation,
        ErrorCode code, string detail, string principal = Root)
    {
        var raw = Raw(db);
        var rollups = Rollups(db);
        var id = Guid.NewGuid();
        var first = Commit(db, id, mutation, principal);
        await Assert.That(first.Error).IsEqualTo(code);
        await Assert.That(first.SafeDetail).IsEqualTo(detail);
        await Assert.That(first.Json).IsNull();
        await Assert.That(Raw(db)).IsEqualTo(raw);
        await Assert.That(Rollups(db)).IsEqualTo(rollups);
        var image = Image(db);
        var position = db.Store.Position;
        var retry = Commit(db, id, mutation, principal);
        await Assert.That(Outcome(retry)).IsEqualTo(Outcome(first));
        await Assert.That(Image(db)).IsEqualTo(image);
        await Assert.That(db.Store.Position).IsEqualTo(position);
    }
}
