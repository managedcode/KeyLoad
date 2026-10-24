using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Owns one commit's staged entries and the reset point of its current action.</summary>
/// <remarks>Without a started action the whole commit is one action. After <see cref="BeginAction"/> the entries
/// staged earlier stay visible, while the frame budget, validation and reset cover only the keys the current action
/// staged itself.</remarks>
internal sealed class ZoneTreeTransactionStaging(int maxFrameBytes)
{
    // An upper bound on the codec's per-record field headers and length prefixes, and on the frame's own envelope.
    private const int RecordEncodingBound = 64;
    private const int FrameEncodingBound = 4_096;
    private readonly SortedDictionary<byte[], ZoneTreeStagedEntry?> actionOriginals = new(BinaryKeyComparer.Instance);
    private long actionBytes;
    private long stagedBytes;

    internal SortedSet<ZoneTreeStagedEntry> Entries { get; } = new(ZoneTreeStagedEntryComparer.Instance);

    /// <summary>Gets whether entries of earlier actions share the commit frame with the current action.</summary>
    internal bool SharesFrame
    {
        get; private set;
    }

    /// <summary>Gets a counter that changes with every staged or discarded write.</summary>
    internal long Version
    {
        get; private set;
    }

    internal bool FitsOneFrame
        => stagedBytes + (long)Entries.Count * RecordEncodingBound + FrameEncodingBound <= maxFrameBytes;

    internal void BeginAction()
    {
        actionOriginals.Clear();
        actionBytes = 0;
        SharesFrame = Entries.Count > 0;
    }

    internal void Stage(byte[] key, byte[]? value)
    {
        var replacing = Entries.TryGetValue(new(key, null), out var previous);
        var touched = actionOriginals.ContainsKey(key);
        var next = actionBytes - (touched ? MutationBytes(previous!) : 0) + MutationBytes(key, value);
        if (next > maxFrameBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, TransactionFrameLimitExceeded);
        }

        if (!touched)
        {
            actionOriginals.Add(key.ToArray(), replacing ? previous : null);
        }
        if (replacing)
        {
            Entries.Remove(previous!);
            stagedBytes -= MutationBytes(previous!);
        }

        Entries.Add(new(key.ToArray(), value?.ToArray()));
        stagedBytes += MutationBytes(key, value);
        actionBytes = next;
        Version++;
    }

    /// <summary>Restores every key the current action staged to its entry before the action.</summary>
    internal void ResetAction()
    {
        foreach (var (key, original) in actionOriginals)
        {
            if (Entries.TryGetValue(new(key, null), out var current))
            {
                Entries.Remove(current);
                stagedBytes -= MutationBytes(current);
            }
            if (original is not null)
            {
                Entries.Add(original);
                stagedBytes += MutationBytes(original);
            }
        }

        actionOriginals.Clear();
        actionBytes = 0;
        Version++;
    }

    /// <summary>Returns the current action's own entries in key order.</summary>
    internal IEnumerable<ZoneTreeStagedEntry> ActionEntries()
    {
        foreach (var key in actionOriginals.Keys)
        {
            if (Entries.TryGetValue(new(key, null), out var current))
            {
                yield return current;
            }
        }
    }

    /// <summary>Returns the entries staged by earlier actions that the current action has not replaced.</summary>
    internal IEnumerable<ZoneTreeStagedEntry> EarlierEntries()
        => Entries.Where(entry => !actionOriginals.ContainsKey(entry.Key));

    internal static StorageMutation[] Mutations(IEnumerable<ZoneTreeStagedEntry> entries)
        => [.. entries.Select(pair => new StorageMutation(pair.Key,
            pair.Value is null ? (ReadOnlyMemory<byte>?)null : new ReadOnlyMemory<byte>(pair.Value)))];

    private static long MutationBytes(ZoneTreeStagedEntry entry) => MutationBytes(entry.Key, entry.Value);

    private static long MutationBytes(byte[] key, byte[]? value) => key.LongLength + (value?.LongLength ?? 0);
}
