namespace KeyLoad.Storage.ZoneTree;

/// <summary>Preflights the same deterministic read debt as actual native recovery before any replacement.</summary>
internal static class ZoneTreeCheckpointPublication
{
    private const long FileStart = 0;
    private const string InvalidStream = "Native checkpoint publication requires its original readable seekable stream.";

    internal static void RequireStream(FileStream stream)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(InvalidStream, nameof(stream));
        }
    }

    internal static StorageSnapshot RequireReplayable(FileStream stream, ZoneTreeStoreOptions options,
        ResourceRetirementConservationFloorV1? requiredFloor = null, ZoneTreeConservationBudget? work = null)
    {
        RequireStream(stream);
        if (stream.Length > options.MaxSnapshotBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ZoneTreePersistenceFormat.SnapshotBytesExceeded);
        }
        stream.Position = FileStart;
        return ZoneTreeCheckpointReader.Read(stream, options, static _ => { }, requiredFloor: requiredFloor, work: work);
    }
}
