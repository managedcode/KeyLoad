using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class SampleRetentionCrashScenario
{
    private const int FourthSampleIndex = 3;
    private const int FourthSampleValue = 8;
    private const string MissingSampleIdentityMessage = "The seeded sample identity is missing.";
    private const string MissingSampleSequenceMessage = "The seeded sample sequence is missing.";

    private const int CorpusStartYear = 2026;
    private const int CorpusStartMonth = 10;
    private const int CorpusStartDay = 1;
    private const int CorpusStartHour = 0;
    private const int CorpusStartMinute = 0;
    private const int CorpusStartSecond = 0;
    private const int RetentionCutoffMinutes = 3;
    private const string ExpiryCommandIdText = "f526509a-5b21-4e9d-8e3e-8751bbd1ea4a";
    private const string EventIdPrefix = "retention-event-";
    private const int FirstSampleIndex = 0;
    private const int FirstSampleValue = 1;
    private const int SecondSampleValue = 2;
    private const int ThirdSampleValue = 4;
    private const string BinaryFileExtension = ".bin";
    private const string SampleNamespace = "sample";
    private const int FirstSampleSequence = 1;
    private const string SampleIdentityNamespace = "sample-id";
    private const string SampleSequenceNamespace = "sample-sequence";

    internal const string Mode = "sample-retention";
    internal const string OperationFile = "sample-retention-operation.bin";
    internal const string SampleFilePrefix = "sample-retention-point-";
    internal const string IdentityFilePrefix = "sample-retention-id-";
    internal const string SequenceFile = "sample-retention-sequence.bin";
    internal const string SeriesSet = "metrics";
    internal const string SeriesId = "recovery-series";
    internal const string Tags = "{}";
    internal const int SampleCount = 4;
    internal const int FirstPageDeletes = 2;
    internal const int RepeatPageDeletes = 2;

    internal static PartitionRef Partition { get; } = new(CrashFixtureValues.Tenant,
        CrashFixtureValues.Database, CrashFixtureValues.Orders, CrashFixtureValues.Partition);

    internal static DateTimeOffset Start { get; } = new(CorpusStartYear, CorpusStartMonth, CorpusStartDay, CorpusStartHour, CorpusStartMinute, CorpusStartSecond, TimeSpan.Zero);
    internal static DateTimeOffset Cutoff { get; } = Start.AddMinutes(RetentionCutoffMinutes);
    internal static Guid ExpiryCommandId { get; } = Guid.Parse(ExpiryCommandIdText);

    internal static string EventId(int index) => EventIdPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    internal static double Value(int index) => index switch { FirstSampleIndex => FirstSampleValue, FirstSampleValue => SecondSampleValue, SecondSampleValue => ThirdSampleValue, FourthSampleIndex => FourthSampleValue, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    internal static DateTimeOffset Timestamp(int index) => Start.AddMinutes(index);
    internal static string SampleFile(int index) => SampleFilePrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + BinaryFileExtension;
    internal static string IdentityFile(int index) => IdentityFilePrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + BinaryFileExtension;

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        const int PositionStep = 1;

        var database = CrashDatabase.Create(store);
        Seed(database);
        await PersistSourceBytesAsync(directory, store);
        var command = new CommandRequest(ExpiryCommandId, Partition,
            [new ExpireSamples(SeriesSet, SeriesId, Cutoff, FirstPageDeletes)]);
        var operation = CrashDatabase.Operation(OperationKind.Batch, command, ExpiryCommandId);
        await File.WriteAllBytesAsync(Path.Combine(directory, OperationFile), NativeSerialization.Serialize(operation));
        boundary.Position = checked(store.Position + PositionStep);
        boundary.Armed = true;
        _ = database.Apply(operation).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }

    private static void Seed(DatabaseEngine database)
    {
        const string AppendCommandIdText = "af84ffbe-59ad-4211-8b75-81460523bd1c";
        const int FirstSampleIndex = 0;

        CrashDatabase.Submit(database, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId,
                new(SeriesSet, ResourceKind.TimeSeries, Partition.TransactionDomainId)), Guid.NewGuid()).Get<ResourceDefinition>();
        var appendId = Guid.Parse(AppendCommandIdText);
        var samples = Enumerable.Range(FirstSampleIndex, SampleCount)
            .Select(index => new SampleData(EventId(index), Timestamp(index), Value(index)))
            .ToImmutableArray();
        var append = new CommandRequest(appendId, Partition, [new AppendSamples(SeriesSet, SeriesId, samples, Tags)]);
        CrashDatabase.Submit(database, OperationKind.Batch, append, appendId).Get<CommitReceipt>();
    }

    private static async Task PersistSourceBytesAsync(string directory, ZoneTreeStore store)
    {
        const int ReadEmptyCount = 0;
        const string MessageText = "The seeded sample point is missing.";
        const int IndexInitialValue = 0;

        var source = store.Read(view =>
        {
            var points = new byte[SampleCount][];
            var identities = new byte[SampleCount][];
            for (var index = ReadEmptyCount; index < SampleCount; index++)
            {
                points[index] = view.ReadOwnedValue(SampleKey(index))
                    ?? throw new InvalidDataException(MessageText);
                identities[index] = view.ReadOwnedValue(IdentityKey(index))
                    ?? throw new InvalidDataException(MissingSampleIdentityMessage);
            }
            var sequence = view.ReadOwnedValue(SequenceKey())
                ?? throw new InvalidDataException(MissingSampleSequenceMessage);
            return (Points: points, Identities: identities, Sequence: sequence);
        });

        for (var index = IndexInitialValue; index < SampleCount; index++)
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, SampleFile(index)), source.Points[index]);
            await File.WriteAllBytesAsync(Path.Combine(directory, IdentityFile(index)), source.Identities[index]);
        }
        await File.WriteAllBytesAsync(Path.Combine(directory, SequenceFile), source.Sequence);
    }

    internal static byte[] SampleKey(int index) => KeySpace.Partition(SampleNamespace, Partition, SeriesSet,
        SeriesId, Timestamp(index), (long)index + FirstSampleSequence);

    internal static byte[] IdentityKey(int index) => KeySpace.Partition(SampleIdentityNamespace, Partition, SeriesSet,
        SeriesId, EventId(index));

    internal static byte[] SequenceKey() => KeySpace.Partition(SampleSequenceNamespace, Partition, SeriesSet, SeriesId);
}
