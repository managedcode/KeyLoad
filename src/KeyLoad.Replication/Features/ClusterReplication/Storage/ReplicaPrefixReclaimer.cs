using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

internal static class ReplicaPrefixReclaimer
{
    private const int FirstLogPosition = 1;
    private const int NoReclaimedEntries = 0;
    private const string WorkLimit = "Replica prefix reclamation exceeds its native byte budget.";
    private static readonly byte[] Prefix = KeyCodec.Encode(ReplicaProtocol.EntryKey);

    internal static bool HasPrefix(IAtomicStore store, ReplicaSnapshot snapshot, ReplicaConfiguration configuration)
        => store.Read(view => view.VisitRange(Prefix, FirstLogPosition, static (_, _) => false,
            untilKey: UpperBound(snapshot), observer: Charge(configuration)).Records > NoReclaimedEntries);

    internal static int Reclaim(IAtomicStore store, ReplicaSnapshot snapshot,
        ReplicaConfiguration configuration, DatabaseEngine? canonicalDatabase, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var removed = HasPrefix(store, snapshot, configuration) ? store.Commit((transaction, _) =>
        {
            var keys = Collect(transaction, snapshot, configuration, canonicalDatabase, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var key in keys)
            { transaction.Delete(key); }
            return keys.Count;
        }) : NoReclaimedEntries;
        if (removed == NoReclaimedEntries)
        { cancellationToken.ThrowIfCancellationRequested(); }
        // Also settle an interrupted rewrite after deletion already committed in an earlier process.
        // After deletion publishes, join the native rewrite even if caller cancellation arrives.
        _ = store.Compact();
        return removed;
    }

    private static List<byte[]> Collect(IKeyValueView view, ReplicaSnapshot snapshot,
        ReplicaConfiguration configuration, DatabaseEngine? canonicalDatabase, CancellationToken cancellationToken)
    {
        var keys = new List<byte[]>(configuration.MaxAppendEntries);
        _ = view.VisitRange(Prefix, configuration.MaxAppendEntries, (key, value) =>
        {
            var entry = ReplicaProtocolCodec.DeserializeStored<ReplicaEntry>(value.ToArray(), configuration.MaxAppendEntries);
            if (entry.Index < FirstLogPosition || entry.Index > snapshot.Index
                || entry.Term < FirstLogPosition || entry.Term > snapshot.Term || !key.SequenceEqual(ReplicaProtocol.EntryStorageKey(entry.Index)))
            { throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog); }
            _ = ReplicaOperationAuthority.Own(entry, canonicalDatabase);
            keys.Add(key.ToArray());
            return true;
        }, untilKey: UpperBound(snapshot), observer: Charge(configuration), cancellationToken: cancellationToken);
        return keys;
    }

    private static StorageReadObserver Charge(ReplicaConfiguration configuration)
    {
        long bytes = NoReclaimedEntries;
        return examined =>
        {
            bytes = checked(bytes + examined);
            if (bytes > configuration.MaxAppendBytes)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, WorkLimit); }
        };
    }

    private static byte[]? UpperBound(ReplicaSnapshot snapshot)
        => snapshot.Index == long.MaxValue ? null : ReplicaProtocol.EntryStorageKey(checked(snapshot.Index + FirstLogPosition));
}
