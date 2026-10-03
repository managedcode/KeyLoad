using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class SampleRetentionCrashScenario
{
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

    internal static DateTimeOffset Start { get; } = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    internal static DateTimeOffset Cutoff { get; } = Start.AddMinutes(3);
    internal static Guid ExpiryCommandId { get; } = Guid.Parse("f526509a-5b21-4e9d-8e3e-8751bbd1ea4a");

    internal static string EventId(int index) => "retention-event-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    internal static double Value(int index) => index switch { 0 => 1, 1 => 2, 2 => 4, 3 => 8, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    internal static DateTimeOffset Timestamp(int index) => Start.AddMinutes(index);
    internal static string SampleFile(int index) => SampleFilePrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".bin";
    internal static string IdentityFile(int index) => IdentityFilePrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".bin";

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var database = CrashDatabase.Create(store);
        Seed(database);
        await PersistSourceBytesAsync(directory, store);
        var command = new CommandRequest(ExpiryCommandId, Partition,
            [new ExpireSamples(SeriesSet, SeriesId, Cutoff, FirstPageDeletes)]);
        var operation = CrashDatabase.Operation(OperationKind.Batch, command, ExpiryCommandId);
        await File.WriteAllBytesAsync(Path.Combine(directory, OperationFile), NativeSerialization.Serialize(operation));
        boundary.Position = checked(store.Position + 1);
        boundary.Armed = true;
        _ = database.Apply(operation).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }

    private static void Seed(DatabaseEngine database)
    {
        CrashDatabase.Submit(database, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId,
                new(SeriesSet, ResourceKind.TimeSeries, Partition.TransactionDomainId)), Guid.NewGuid()).Get<ResourceDefinition>();
        var appendId = Guid.Parse("af84ffbe-59ad-4211-8b75-81460523bd1c");
        var samples = Enumerable.Range(0, SampleCount)
            .Select(index => new SampleData(EventId(index), Timestamp(index), Value(index)))
            .ToImmutableArray();
        var append = new CommandRequest(appendId, Partition, [new AppendSamples(SeriesSet, SeriesId, samples, Tags)]);
        CrashDatabase.Submit(database, OperationKind.Batch, append, appendId).Get<CommitReceipt>();
    }

    private static async Task PersistSourceBytesAsync(string directory, ZoneTreeStore store)
    {
        var source = store.Read(view =>
        {
            var points = new byte[SampleCount][];
            var identities = new byte[SampleCount][];
            for (var index = 0; index < SampleCount; index++)
            {
                points[index] = view.ReadOwnedValue(SampleKey(index))
                    ?? throw new InvalidDataException("The seeded sample point is missing.");
                identities[index] = view.ReadOwnedValue(IdentityKey(index))
                    ?? throw new InvalidDataException("The seeded sample identity is missing.");
            }
            var sequence = view.ReadOwnedValue(SequenceKey())
                ?? throw new InvalidDataException("The seeded sample sequence is missing.");
            return (Points: points, Identities: identities, Sequence: sequence);
        });

        for (var index = 0; index < SampleCount; index++)
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, SampleFile(index)), source.Points[index]);
            await File.WriteAllBytesAsync(Path.Combine(directory, IdentityFile(index)), source.Identities[index]);
        }
        await File.WriteAllBytesAsync(Path.Combine(directory, SequenceFile), source.Sequence);
    }

    internal static byte[] SampleKey(int index) => KeySpace.Partition("sample", Partition, SeriesSet,
        SeriesId, Timestamp(index), (long)index + 1);

    internal static byte[] IdentityKey(int index) => KeySpace.Partition("sample-id", Partition, SeriesSet,
        SeriesId, EventId(index));

    internal static byte[] SequenceKey() => KeySpace.Partition("sample-sequence", Partition, SeriesSet, SeriesId);
}
