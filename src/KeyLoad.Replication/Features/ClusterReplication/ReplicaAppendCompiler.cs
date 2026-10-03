using KeyLoad.Core;

namespace KeyLoad.Replication;

internal static class ReplicaAppendCompiler
{
    internal static ReplicaAppendBatch Validate(IReadOnlyList<ReplicaEntry> entries, ReplicaHardState state, ReplicaConfiguration configuration,
        Func<long, long> termAt, DatabaseEngine? canonicalDatabase)
    {
        var owned = OwnEntries(entries, state, configuration, termAt, canonicalDatabase);
        if (ReplicaProtocolCodec.MeasureEntries(owned) > configuration.MaxAppendBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaPersistence.ReadLimit);
        }
        return new(owned, Encode(owned, configuration.MaxAppendBytes));
    }

    private static ReplicaEntry[] OwnEntries(IReadOnlyList<ReplicaEntry> entries, ReplicaHardState state,
        ReplicaConfiguration configuration, Func<long, long> termAt, DatabaseEngine? canonicalDatabase)
    {
        if (entries.Count == 0 || entries.Count > configuration.MaxAppendEntries)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
        var owned = entries.ToArray();
        if (owned.Length == 0 || owned.Length > configuration.MaxAppendEntries || owned[0] is not { } firstEntry)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
        var first = firstEntry.Index;
        if (first <= (state.Snapshot?.Index ?? 0)
            || first > state.LastIndex && first - state.LastIndex != 1 || owned.Length - 1L > long.MaxValue - first)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
        var previousTerm = termAt(first - 1);
        for (var position = 0; position < owned.Length; position++)
        {
            var entry = owned[position];
            if (entry is null || entry.Index != first + position || entry.Term <= 0 || entry.Term > state.Term || entry.Term < previousTerm)
            {
                throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
            }
            owned[position] = ReplicaOperationAuthority.Own(entry, canonicalDatabase);
            previousTerm = entry.Term;
        }
        return owned;
    }

    private static byte[][] Encode(ReplicaEntry[] entries, int maximumBytes)
    {
        var encoded = new byte[entries.Length][];
        for (var position = 0; position < entries.Length; position++)
        {
            encoded[position] = ReplicaProtocolCodec.Serialize(entries[position]);
            if (encoded[position].Length > maximumBytes)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaPersistence.ReadLimit);
            }
        }
        return encoded;
    }

    internal static ReplicaHardState NextState(IReadOnlyList<ReplicaEntry> entries, ReplicaHardState state,
        Func<long, ReplicaEntry> load, DatabaseEngine? canonicalDatabase)
    {
        var conflict = false;
        foreach (var entry in entries)
        {
            if (entry.Index > state.LastIndex || !ReplicaOperationAuthority.Equal(load(entry.Index), entry, canonicalDatabase))
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

internal sealed record ReplicaAppendBatch(ReplicaEntry[] Entries, byte[][] Encoded);
