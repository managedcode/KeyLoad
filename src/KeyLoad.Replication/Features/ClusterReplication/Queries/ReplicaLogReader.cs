using System.Collections.Immutable;

namespace KeyLoad.Replication;

internal static class ReplicaLogReader
{
    private const int MinimumReadEntryCount = 1;
    private const int MinimumReadByteCount = 1;
    private const int BeforeFirstLogPosition = 0;
    private const int LastArrayElementOffset = 1;

    internal static ImmutableArray<ReplicaEntry> Read(long firstIndex, int maxEntries, int maxBytes,
        ReplicaConfiguration configuration, ReplicaHardState state, Func<long, ReplicaEntry> load)
    {
        if (maxEntries < MinimumReadEntryCount || maxEntries > configuration.MaxAppendEntries || maxBytes < MinimumReadByteCount || maxBytes > configuration.MaxAppendBytes
            || firstIndex <= (state.Snapshot?.Index ?? BeforeFirstLogPosition))
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
        var entries = new List<ReplicaEntry>();
        var index = firstIndex;
        while (index <= state.LastIndex && entries.Count < maxEntries)
        {
            var entry = load(index);
            if (ReplicaProtocolCodec.MeasureEntries([entry]) > maxBytes)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaPersistence.ReadLimit); }
            entries.Add(entry);
            if (ReplicaProtocolCodec.MeasureEntries(entries) > maxBytes)
            {
                entries.RemoveAt(entries.Count - LastArrayElementOffset);
                break;
            }
            if (index == long.MaxValue)
            { break; }
            index++;
        }
        return [.. entries];
    }
}
