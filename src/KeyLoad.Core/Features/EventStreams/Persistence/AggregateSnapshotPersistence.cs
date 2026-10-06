namespace KeyLoad.Core;

/// <summary>Stores the native-format discriminator and checksummed public snapshot contract.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(AggregateSnapshotPersistence.SnapshotEnvelopeAlias)]
internal sealed record AggregateSnapshotEnvelope(
    [property: Orleans.Id(0)] int FormatVersion,
    [property: Orleans.Id(1)] AggregateSnapshotState Snapshot);

internal static class AggregateSnapshotPersistence
{
    internal const int CurrentFormatVersion = 1;
    internal const string SnapshotEnvelopeAlias = "keyload.core.v1.AggregateSnapshotEnvelope";
    internal const string SnapshotSpace = "aggregate-snapshot-v1";
    private const string UnknownFormatMessage = "The aggregate snapshot format is unsupported.";
    private const string InvalidSnapshotMessage = "The aggregate snapshot is corrupt.";

    internal static byte[] Key(StreamRef stream)
        => KeySpace.Partition(SnapshotSpace, stream.Partition, stream.StreamSet, stream.StreamId, stream.Generation);

    internal static AggregateSnapshotState Create(StreamRef stream, long version, long sourceRevision,
        string reducerVersion, int stateSchemaVersion, string stateJson)
    {
        var snapshot = new AggregateSnapshotState(stream, version, sourceRevision, reducerVersion,
            stateSchemaVersion, stateJson, string.Empty);
        // The checksum identity is the exact public snapshot with Checksum empty; StateJson remains an exact string value.
        return snapshot with { Checksum = JsonData.Fingerprint(snapshot) };
    }

    internal static byte[] Serialize(AggregateSnapshotState snapshot)
        => NativeSerialization.Serialize(new AggregateSnapshotEnvelope(CurrentFormatVersion, snapshot));

    internal static AggregateSnapshotState Deserialize(ReadOnlySpan<byte> value, DatabaseLimits limits)
    {
        const int SnapshotVersionValidationBoundary = 1;
        const int SourceRevisionValidationBoundary = 0;
        const int StateSchemaVersionValidationBoundary = 1;

        var envelope = NativeSerialization.Deserialize<AggregateSnapshotEnvelope>(value);
        if (envelope.FormatVersion != CurrentFormatVersion)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, UnknownFormatMessage);
        }
        var snapshot = envelope.Snapshot;
        if (snapshot.SnapshotVersion < SnapshotVersionValidationBoundary || snapshot.SourceRevision < SourceRevisionValidationBoundary
            || snapshot.StateSchemaVersion < StateSchemaVersionValidationBoundary || string.IsNullOrWhiteSpace(snapshot.ReducerVersion)
            || snapshot.StateJson is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidSnapshotMessage);
        }
        try
        {
            _ = JsonData.Validate(snapshot.StateJson, limits, requireObject: false);
        }
        catch (KeyLoadException)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidSnapshotMessage);
        }
        if (!string.Equals(snapshot.Checksum, JsonData.Fingerprint(snapshot with { Checksum = string.Empty }),
            StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidSnapshotMessage);
        }
        return snapshot;
    }
}
