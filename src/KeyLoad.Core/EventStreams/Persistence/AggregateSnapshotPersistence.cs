namespace KeyLoad.Core;

internal static class AggregateSnapshotPersistence
{
    internal const string SnapshotSpace = "aggregate-snapshot";
    private const string InvalidSnapshotMessage = "The aggregate snapshot is corrupt.";

    internal static byte[] Key(StreamRef stream)
        => KeySpace.Partition(SnapshotSpace, stream.Partition, stream.StreamSet, stream.StreamId, stream.Generation);

    internal static AggregateSnapshotState Create(StreamRef stream, long version, long sourceRevision,
        string reducerVersion, int stateSchemaVersion, string stateJson)
    {
        var snapshot = new AggregateSnapshotState(stream, version, sourceRevision, reducerVersion,
            stateSchemaVersion, stateJson, string.Empty);
        // The checksum identity is the exact public snapshot with Checksum empty; StateJson remains an exact string value.
        return snapshot with
        {
            Checksum = JsonData.Fingerprint(snapshot)
        };
    }

    internal static byte[] Serialize(AggregateSnapshotState snapshot)
        => OrleansSerialization.Serialize(snapshot);

    internal static AggregateSnapshotState Deserialize(ReadOnlySpan<byte> value)
    {
        const int SnapshotVersionValidationBoundary = 1;
        const int SourceRevisionValidationBoundary = 0;
        const int StateSchemaVersionValidationBoundary = 1;

        var snapshot = OrleansSerialization.Deserialize<AggregateSnapshotState>(value);
        if (snapshot.SnapshotVersion < SnapshotVersionValidationBoundary || snapshot.SourceRevision < SourceRevisionValidationBoundary
            || snapshot.StateSchemaVersion < StateSchemaVersionValidationBoundary || string.IsNullOrWhiteSpace(snapshot.ReducerVersion)
            || snapshot.StateJson is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidSnapshotMessage);
        }
        try
        {
            _ = JsonData.Validate(snapshot.StateJson, requireObject: false);
        }
        catch (KeyLoadException)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidSnapshotMessage);
        }
        if (!string.Equals(snapshot.Checksum, JsonData.Fingerprint(snapshot with
        {
            Checksum = string.Empty
        }),
            StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidSnapshotMessage);
        }
        return snapshot;
    }
}
