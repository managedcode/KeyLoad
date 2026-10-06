using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkStoreFixture : IDisposable
{
    internal const string Set = "chunk-store-metrics";
    internal const string Series = "chunk-store-cpu";
    internal const string Root = "root";
    internal const int MaximumCanonicalRecords = 10_000;
    internal const int MaximumSeriesRecords = 256;
    internal const int WindowLimit = 3;
    internal static readonly DateTimeOffset Floor = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset FirstEqualUtc = Floor.AddMinutes(1);
    internal static readonly DateTimeOffset EndExclusive = Floor.AddMinutes(3);
    internal static readonly TimeSpan WindowWidth = TimeSpan.FromMinutes(1);
    internal const string Tags = "{\"label\":\"café\",\"unit\":\"°C\"}";

    internal TestDatabase Database { get; }

    private SampleChunkStoreFixture(TestDatabase database) => Database = database;

    internal static SampleChunkStoreFixture Create()
    {
        var database = new TestDatabase();
        try
        {
            database.Configure(Set, ResourceKind.TimeSeries);
            AppendInitial(database);
            AppendLateAndEqual(database);
            database.Commit(new ExpireSamples(Set, Series, Floor, SampleRetentionDefaults.MaximumDeletes));
            AppendRetainedDuplicate(database);
            return new(database);
        }
        catch (Exception)
        {
            database.Dispose();
            throw;
        }
    }

    internal SampleChunkStoreSourceCut CaptureCodecCut()
        => CaptureCodecCut(Database.Store, Database.Database, Database.Partition);

    internal static SampleChunkStoreSourceCut CaptureCodecCut(ZoneTreeStore store,
        DatabaseEngine database, PartitionRef partition)
    {
        return store.Read(view =>
        {
            var budget = NewBudget(database);
            var before = CaptureCanonicalState(store, database, view);
            var source = ReadSamples(partition, view, budget);
            var encoded = SampleChunkCodec.Encode(source.AsSpan(), budget);
            budget.ChargeBytes(encoded.Length);
            var decoded = SampleChunkCodec.Decode(encoded, budget);
            var readers = ReadAllFour(database, partition, view);
            return new SampleChunkStoreSourceCut(source, decoded, encoded, readers, before);
        });
    }

    internal static SampleChunkStoreCanonicalState CaptureCanonicalState(TestDatabase database)
    {
        return CaptureCanonicalState(database.Store, database.Database);
    }

    internal static SampleChunkStoreCanonicalState CaptureCanonicalState(ZoneTreeStore store, DatabaseEngine database)
        => store.Read(view => CaptureCanonicalState(store, database, view));

    private static SampleChunkStoreCanonicalState CaptureCanonicalState(ZoneTreeStore store,
        DatabaseEngine database, IKeyValueView view)
    {
        var position = store.Position;
        var page = NewBudget(database).Scan(view, [], MaximumCanonicalRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The canonical test fixture exceeded its complete-state bound.");
        }

        return new(position, page.Records.ToArray());
    }

    internal static KeyValueRecord[] CaptureSeriesAuthority(TestDatabase database)
    {
        return database.Store.Read(view =>
        {
            var budget = NewBudget(database);
            var records = new List<KeyValueRecord>();
            AddPrefix(view, budget, records, SampleReadKeys.Prefix(database.Partition, Set, Series));
            AddPrefix(view, budget, records, KeySpace.Partition("sample-id", database.Partition, Set, Series));
            AddPoint(view, budget, records, KeySpace.Partition("sample-sequence", database.Partition, Set, Series));
            AddPoint(view, budget, records, SampleRetentionStateReader.Key(database.Partition, Set, Series));
            return records.ToArray();
        });
    }

    internal static byte[] SampleIdKey(TestDatabase database, string eventId)
        => KeySpace.Partition("sample-id", database.Partition, Set, Series, eventId);

    private static void AddPrefix(IKeyValueView view, ReadExecutionBudget budget, List<KeyValueRecord> records,
        byte[] prefix)
    {
        var page = budget.Scan(view, prefix, MaximumCanonicalRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The canonical series authority exceeded its complete-state bound.");
        }

        records.AddRange(page.Records);
    }

    private static void AddPoint(IKeyValueView view, ReadExecutionBudget budget,
        List<KeyValueRecord> records, byte[] key)
    {
        if (budget.Read(view, key) is { } value)
        {
            records.Add(new(key, value));
        }
    }

    internal static ReadExecutionBudget NewBudget(TestDatabase database, CancellationToken cancellationToken = default)
        => new(UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Database.EvaluationClock, cancellationToken);

    private static ReadExecutionBudget NewBudget(DatabaseEngine database)
        => new(UnitExecutionOptions.DatabaseLimits(database.Limits), database.EvaluationClock);

    private static SampleRecord[] ReadSamples(PartitionRef partition, IKeyValueView view, ReadExecutionBudget budget)
    {
        var records = new List<SampleRecord>();
        var scan = budget.VisitRange(view, SampleReadKeys.Prefix(partition, Set, Series), MaximumSeriesRecords,
            (_, value) =>
            {
                records.Add(NativeSerialization.Deserialize<SampleRecord>(value));
                return true;
            });
        if (scan.HasMore)
        {
            throw new InvalidOperationException("The canonical series exceeded its complete-sample bound.");
        }

        return records.ToArray();
    }

    private static SampleChunkStoreReaderResults ReadAllFour(DatabaseEngine engine, PartitionRef partition,
        IKeyValueView view)
    {
        var from = Floor;
        var range = SampleRangeReader.Read(engine, engine.EvaluationClock, view, Root,
            new(partition, Set, Series, from, EndExclusive, MaximumSeriesRecords), NewBudget(engine));
        var latest = SampleLatestReader.Read(engine, view, Root,
            new(partition, Set, Series, EndExclusive), NewBudget(engine));
        var aggregate = SampleAggregateReader.Read(engine, view, Root,
            new(partition, Set, Series, from, EndExclusive, MaximumSeriesRecords), NewBudget(engine));
        var windows = SampleAggregateWindowReader.Read(engine, view, Root,
            new(partition, Set, Series, from, EndExclusive, WindowWidth, MaximumSeriesRecords, WindowLimit),
            NewBudget(engine));
        return new(range, latest.Sample, aggregate, windows.Windows.ToArray());
    }

    private static void AppendInitial(TestDatabase database)
    {
        Append(database,
            Data("chunk-old", Floor.AddMinutes(-1), 8d),
            Data("chunk-a", FirstEqualUtc, 1d, TimeSpan.Zero),
            Data("chunk-b", FirstEqualUtc.ToOffset(TimeSpan.FromHours(5)), 2d),
            Data("chunk-c", Floor.AddMinutes(2), -0d));
    }

    private static void AppendLateAndEqual(TestDatabase database)
        => Append(database,
            Data("chunk-late", Floor.AddSeconds(30), 4d),
            Data("chunk-equal-late", FirstEqualUtc, -2d),
            Data("chunk-end", EndExclusive, 8d));

    private static void AppendRetainedDuplicate(TestDatabase database)
        => Append(database,
            Data("chunk-old", Floor.AddMinutes(-1), 8d),
            Data("chunk-b", FirstEqualUtc.ToOffset(TimeSpan.FromHours(5)), 2d));

    private static void Append(TestDatabase database, params SampleData[] samples)
        => database.Commit(new AppendSamples(Set, Series, [.. samples], Tags));

    private static SampleData Data(string id, DateTimeOffset timestamp, double value, TimeSpan? offset = null)
        => new(id, offset is { } requested ? timestamp.ToOffset(requested) : timestamp, value);

    public void Dispose() => Database.Dispose();

}

internal sealed record SampleChunkStoreSourceCut(SampleRecord[] Source, SampleRecord[] Decoded,
    byte[] Encoded, SampleChunkStoreReaderResults Readers, SampleChunkStoreCanonicalState CanonicalState);

internal sealed record SampleChunkStoreReaderResults(SampleRecord[] Range, SampleRecord? Latest,
    SampleAggregate Aggregate, SampleAggregateWindow[] Windows);

internal sealed record SampleChunkStoreCanonicalState(long Position, KeyValueRecord[] Records);
