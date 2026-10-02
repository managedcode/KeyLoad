namespace KeyLoad.Replication;

internal static class ReplicaAppendCompiler
{
    internal static byte[][] Validate(IReadOnlyList<ReplicaEntry> entries, ReplicaHardState state, ReplicaConfiguration configuration, Func<long, long> termAt)
    {
        var first = entries[0].Index;
        if (entries.Count > configuration.MaxAppendEntries || first <= (state.Snapshot?.Index ?? 0)
            || first > state.LastIndex && first - state.LastIndex != 1 || entries.Count - 1L > long.MaxValue - first)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
        var encoded = new byte[entries.Count][];
        var total = 2L;
        var previousTerm = termAt(first - 1);
        for (var position = 0; position < entries.Count; position++)
        {
            var entry = entries[position];
            if (entry.Index != first + position || entry.Term <= 0 || entry.Term > state.Term || entry.Term < previousTerm)
            {
                throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
            }
            encoded[position] = ReplicaProtocolCodec.Serialize(entry);
            total += encoded[position].Length + (position == 0 ? 0 : 1);
            if (total > configuration.MaxAppendBytes)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaPersistence.ReadLimit);
            }
            previousTerm = entry.Term;
        }
        return encoded;
    }

    internal static ReplicaHardState NextState(IReadOnlyList<ReplicaEntry> entries, byte[][] encoded, ReplicaHardState state, Func<long, ReplicaEntry> load)
    {
        var conflict = false;
        for (var position = 0; position < entries.Count; position++)
        {
            var entry = entries[position];
            if (entry.Index > state.LastIndex || !ReplicaProtocolCodec.Serialize(load(entry.Index)).AsSpan().SequenceEqual(encoded[position]))
            {
                if (entry.Index <= state.CommittedIndex)
                {
                    throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.InvalidAppend);
                }
                conflict = true;
            }
        }
        return state with { LastIndex = conflict ? entries[^1].Index : state.LastIndex };
    }
}
