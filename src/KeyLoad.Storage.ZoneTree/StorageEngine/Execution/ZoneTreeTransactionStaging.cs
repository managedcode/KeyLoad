namespace KeyLoad.Storage.ZoneTree;

/// <summary>Owns one commit's staged entries and the reset point of its current action.</summary>
/// <remarks>Without a started action the whole commit is one action. After <see cref="BeginAction"/> the entries
/// staged earlier stay visible, while reset covers only the keys the current action staged itself.</remarks>
internal sealed class ZoneTreeTransactionStaging
{
    private readonly SortedDictionary<byte[], ZoneTreeStagedEntry?> actionOriginals = new(BinaryKeyComparer.Instance);

    internal SortedSet<ZoneTreeStagedEntry> Entries { get; } = new(ZoneTreeStagedEntryComparer.Instance);

    /// <summary>Gets a counter that changes with every staged or discarded write.</summary>
    internal long Version
    {
        get; private set;
    }

    internal void BeginAction() => actionOriginals.Clear();

    internal void Stage(byte[] key, byte[]? value)
    {
        var replacing = Entries.TryGetValue(new(key, null), out var previous);
        var ownedKey = replacing ? previous!.Key : key.ToArray();
        actionOriginals.TryAdd(ownedKey, previous);
        if (replacing)
        {
            Entries.Remove(previous!);
        }

        Entries.Add(new(ownedKey, value?.ToArray()));
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
            }
            if (original is not null)
            {
                Entries.Add(original);
            }
        }

        actionOriginals.Clear();
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
}
