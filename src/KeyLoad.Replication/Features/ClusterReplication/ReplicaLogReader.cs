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
        var bytes = 2L;
        var index = firstIndex;
        while (index <= state.LastIndex && entries.Count < maxEntries)
        {
            var entry = load(index);
            var length = ReplicaProtocolCodec.Serialize(entry).Length;
            if (length + 2L > maxBytes)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaPersistence.ReadLimit); }
            var separator = entries.Count == 0 ? 0 : 1;
            if (bytes + length + separator > maxBytes)
            { break; }
            entries.Add(entry);
            bytes += length + separator;
            if (index == long.MaxValue)
            { break; }
            index++;
        }
        return [.. entries];
    }
}
