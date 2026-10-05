using KeyLoad.Core;

namespace KeyLoad.Replication;

internal static class ReplicaAppendCompiler
{
    private const int EmptyEntryCount = 0;
    private const int FirstEntryIndex = 0;
    private const int BeforeFirstLogPosition = 0;
    private const int ContiguousIndexStep = 1;
    private const long TailOffsetWide = 1L;
    private const int UnelectedTerm = 0;
    private const int LastEntryFromEnd = 1;

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
        if (entries.Count == EmptyEntryCount || entries.Count > configuration.MaxAppendEntries)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
        var owned = entries.ToArray();
        if (owned.Length == EmptyEntryCount || owned.Length > configuration.MaxAppendEntries || owned[FirstEntryIndex] is not { } firstEntry)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
        var first = firstEntry.Index;
        if (first <= (state.Snapshot?.Index ?? BeforeFirstLogPosition)
            || first > state.LastIndex && first - state.LastIndex != ContiguousIndexStep || owned.Length - TailOffsetWide > long.MaxValue - first)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
        var previousTerm = termAt(first - ContiguousIndexStep);
        for (var position = FirstEntryIndex; position < owned.Length; position++)
        {
            var entry = owned[position];
            if (entry is null || entry.Index != first + position || entry.Term <= UnelectedTerm || entry.Term > state.Term || entry.Term < previousTerm)
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
        for (var position = FirstEntryIndex; position < entries.Length; position++)
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
        return state with { LastIndex = conflict ? entries[^LastEntryFromEnd].Index : state.LastIndex };
    }
}

internal sealed record ReplicaAppendBatch(ReplicaEntry[] Entries, byte[][] Encoded);
