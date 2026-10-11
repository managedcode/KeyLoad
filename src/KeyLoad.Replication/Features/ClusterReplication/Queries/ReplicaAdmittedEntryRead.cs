using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

internal static class ReplicaAdmittedEntryRead
{
    private const int UnelectedTerm = 0;

    internal static ReplicaEntry Read(IAtomicStore store, long index, long currentTerm, int maximumEntries,
        DatabaseEngine? canonicalDatabase, StorageReadObserver observer)
    {
        byte[]? bytes = null;
        store.Read(view => view.ReadValue(ReplicaProtocol.EntryStorageKey(index),
            borrowed => bytes = borrowed.ToArray(), observer));
        return Decode(bytes ?? throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog),
            index, currentTerm, maximumEntries, canonicalDatabase);
    }

    internal static ReplicaEntry Decode(byte[] originalBytes, long index, long currentTerm,
        int maximumEntries, DatabaseEngine? canonicalDatabase)
    {
        var entry = ReplicaProtocolCodec.DeserializeStored<ReplicaEntry>(originalBytes, maximumEntries);
        if (entry.Index != index || entry.Term <= UnelectedTerm || entry.Term > currentTerm)
        { throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog); }
        return ReplicaOperationAuthority.Own(entry, canonicalDatabase);
    }
}
