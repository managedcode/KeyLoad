using System.Collections.Immutable;

namespace KeyLoad.Replication;

internal static class ReplicaLogReader
{
    internal static ImmutableArray<ReplicaEntry> Read(long firstIndex, int maxEntries, int maxBytes,
        ReplicaConfiguration configuration, ReplicaHardState state, Func<long, ReplicaEntry> load)
    {
        if (maxEntries < 1 || maxEntries > configuration.MaxAppendEntries || maxBytes < 1 || maxBytes > configuration.MaxAppendBytes
            || firstIndex <= (state.Snapshot?.Index ?? 0))
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
                entries.RemoveAt(entries.Count - 1);
                break;
            }
            if (index == long.MaxValue)
            { break; }
            index++;
        }
        return [.. entries];
    }
}
