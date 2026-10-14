using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

internal static class ReplicaLogRecordAccess
{
    private const int FirstEntryIndex = 0;
    private const int BeforeFirstLogPosition = 0;
    private const int UnelectedTerm = 0;

    internal static long Term(IAtomicStore store, long index, ReplicaHardState state,
        ReplicaConfiguration configuration, ref ReplicaTermObservation? termObservation)
    {
        if (index == BeforeFirstLogPosition)
        {
            return UnelectedTerm;
        }
        if (state.Snapshot is { } snapshot && index == snapshot.Index)
        {
            return snapshot.Term;
        }
        if (index < (state.Snapshot?.Index ?? BeforeFirstLogPosition) || index > state.LastIndex || index < BeforeFirstLogPosition)
        {
            throw Errors.Fail(ErrorCode.NotFound, ReplicaPersistence.MissingEntry);
        }
        var result = ReplicaTermObservationReader.Read(store, index, state.Term, configuration.MaxAppendEntries, termObservation);
        termObservation = result.Observation;
        return result.Term;
    }

    internal static ReplicaEntry Load(IAtomicStore store, long index, ref ReplicaHardState state,
        ReplicaConfiguration configuration,
        DatabaseEngine? canonicalDatabase)
    {
        var bytes = store.Read(view => view.ReadOwnedValue(ReplicaProtocol.EntryStorageKey(index)))
            ?? throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
        return ReplicaAdmittedEntryRead.Decode(bytes, index, state.Term, configuration.MaxAppendEntries,
            canonicalDatabase);
    }

    internal static void Append(IAtomicStore store, ReplicaAppendBatch batch, ReplicaHardState next)
    {
        store.Commit((tx, _) =>
        {
            for (var position = FirstEntryIndex; position < batch.Entries.Length; position++)
            {
                tx.Put(ReplicaProtocol.EntryStorageKey(batch.Entries[position].Index), batch.Encoded[position]);
            }
            tx.Put(ReplicaProtocol.StateStorageKey, ReplicaProtocolCodec.Serialize(next));
            return true;
        });
    }

    internal static void Save(IAtomicStore store, ReplicaHardState next)
    {
        store.Commit((tx, _) =>
        {
            tx.Put(ReplicaProtocol.StateStorageKey, ReplicaProtocolCodec.Serialize(next));
            return true;
        });
    }
}
