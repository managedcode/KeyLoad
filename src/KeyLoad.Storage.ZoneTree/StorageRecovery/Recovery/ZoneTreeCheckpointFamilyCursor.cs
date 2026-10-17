namespace KeyLoad.Storage.ZoneTree;

/// <summary>A single bounded native batch cursor over the original already-open image handle.</summary>
internal sealed class ZoneTreeCheckpointFamilyCursor(FileStream input,
    ZoneTreeConservationBudget budget, byte[] prefix, long offset)
{
    private const int First = 0;
    private const int NextRecord = 1;
    private const long Missing = -1;
    private StorageMutation[] batch = [];
    private int next = First;
    private long position = offset;
    internal StorageMutation? Current
    {
        get; private set;
    }

    internal bool MoveNext()
    {
        Current = null;
        while (position != Missing)
        {
            budget.Check();
            if (next == batch.Length && !ReadBatch())
            {
                return false;
            }
            var row = batch[next];
            next += NextRecord;
            budget.Charge(First);
            if (row.Key.Span.StartsWith(prefix))
            {
                Current = row;
                return true;
            }
            if (row.Key.Span.SequenceCompareTo(prefix) > First)
            {
                position = Missing;
                return false;
            }
        }
        return false;
    }

    private bool ReadBatch()
    {
        var original = input.Position;
        try
        {
            input.Position = position;
            var frame = budget.ReadFrame(input);
            position = input.Position;
            if (frame.Magic == ZoneTreePersistenceFormat.CheckpointEndMagic)
            {
                position = Missing;
                return false;
            }
            if (frame.Magic != ZoneTreePersistenceFormat.CheckpointDataMagic)
            {
                throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointFrameTypeInvalid);
            }
            batch = NativeSerialization.Deserialize<StorageMutation[]>(frame.Payload);
            next = First;
            if (batch.Length == First)
            {
                throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointRecordsInvalid);
            }
            return true;
        }
        finally
        {
            input.Position = original;
        }
    }
}
