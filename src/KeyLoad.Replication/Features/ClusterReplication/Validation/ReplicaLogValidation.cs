using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

internal static class ReplicaLogValidation
{
    internal static ReplicaHardState Open(IAtomicStore store, ReplicaConfiguration configuration, DatabaseEngine? canonicalDatabase)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        if (store.Identity.Incarnation != configuration.Incarnation
            || canonicalDatabase is not null && canonicalDatabase.Store.Identity.Incarnation != configuration.Incarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ReplicaProtocol.CorruptLog);
        }
        var metadata = store.Read(view => (State: view.ReadOwnedValue(ReplicaProtocol.StateStorageKey),
            Membership: view.ReadOwnedValue(ReplicaBenchmarkMembership.StorageKey)));
        ReplicaBenchmarkMembership.Validate(metadata.Membership, metadata.State is not null, configuration);
        var state = metadata.State is { } bytes ? ReplicaPersistence.Decode<ReplicaHardState>(bytes, configuration.MaxAppendEntries) : null;
        if (state is null)
        {
            if (store.Read(view => view.Scan(KeyCodec.Encode(ReplicaProtocol.EntryKey), 1).Records.Length) != 0)
            {
                throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
            }
            state = new(ReplicaProtocol.FormatVersion, configuration.Incarnation, 0, null, 0, 0, null);
            store.Commit((tx, _) =>
            {
                tx.Put(ReplicaProtocol.StateStorageKey, ReplicaProtocolCodec.Serialize(state));
                ReplicaBenchmarkMembership.Initialize(tx, configuration);
                return true;
            });
        }
        Validate(state, configuration);
        ValidateEntries(store, state, configuration, canonicalDatabase);
        return state;
    }

    internal static void Validate(ReplicaHardState state, ReplicaConfiguration configuration)
    {
        if (state.Version != ReplicaProtocol.FormatVersion)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, ReplicaProtocol.UnsupportedFormat);
        }
        var snapshotIndex = state.Snapshot?.Index ?? 0;
        if (state.Incarnation != configuration.Incarnation
            || state.Term < 0 || state.LastIndex < snapshotIndex || state.CommittedIndex < snapshotIndex
            || state.CommittedIndex > state.LastIndex || state.Term == 0 && (state.LastIndex != 0 || state.VotedFor is not null)
            || state.VotedFor is { } voter && !configuration.VoterIds.Contains(voter, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
        }
        if (state.Snapshot is { } snapshot)
        {
            try
            { ReplicaPersistence.ValidateSnapshot(snapshot, configuration); }
            catch (KeyLoadException error) when (error.Code == ErrorCode.Validation)
            {
                throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
            }
            if (snapshot.Term > state.Term)
            {
                throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
            }
        }
    }

    private static void ValidateEntries(IAtomicStore store, ReplicaHardState state, ReplicaConfiguration configuration,
        DatabaseEngine? canonicalDatabase)
    {
        var previousTerm = state.Snapshot?.Term ?? 0;
        var index = state.Snapshot?.Index ?? 0;
        while (index < state.LastIndex)
        {
            index++;
            var bytes = store.Read(view => view.ReadOwnedValue(ReplicaProtocol.EntryStorageKey(index)));
            if (bytes is null || bytes.Length > configuration.MaxAppendBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
            }
            var entry = ReplicaProtocolCodec.DeserializeStored<ReplicaEntry>(bytes, configuration.MaxAppendEntries);
            if (entry.Index != index || entry.Term <= 0 || entry.Term > state.Term || entry.Term < previousTerm)
            {
                throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
            }
            entry = ReplicaOperationAuthority.Own(entry, canonicalDatabase);
            previousTerm = entry.Term;
        }
    }
}
