using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

internal static class ReplicaLogValidation
{
    private const int ExistenceProbeEntryCount = 1;
    private const int EmptyEntryCount = 0;
    private const int UnelectedTerm = 0;
    private const int BeforeFirstLogPosition = 0;

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
            if (store.Read(view => view.Scan(KeyCodec.Encode(ReplicaProtocol.EntryKey), ExistenceProbeEntryCount).Records.Length) != EmptyEntryCount)
            {
                throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
            }
            state = new(ReplicaProtocol.FormatVersion, configuration.Incarnation, UnelectedTerm, null, BeforeFirstLogPosition, BeforeFirstLogPosition, null);
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
        var snapshotIndex = state.Snapshot?.Index ?? BeforeFirstLogPosition;
        if (state.Incarnation != configuration.Incarnation
            || state.Term < UnelectedTerm || state.LastIndex < snapshotIndex || state.CommittedIndex < snapshotIndex
            || state.CommittedIndex > state.LastIndex || state.Term == UnelectedTerm && (state.LastIndex != BeforeFirstLogPosition || state.VotedFor is not null)
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
        var previousTerm = state.Snapshot?.Term ?? UnelectedTerm;
        var index = state.Snapshot?.Index ?? BeforeFirstLogPosition;
        while (index < state.LastIndex)
        {
            index++;
            var bytes = store.Read(view => view.ReadOwnedValue(ReplicaProtocol.EntryStorageKey(index)));
            if (bytes is null || bytes.Length > configuration.MaxAppendBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
            }
            var entry = ReplicaProtocolCodec.DeserializeStored<ReplicaEntry>(bytes, configuration.MaxAppendEntries);
            if (entry.Index != index || entry.Term <= UnelectedTerm || entry.Term > state.Term || entry.Term < previousTerm)
            {
                throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
            }
            entry = ReplicaOperationAuthority.Own(entry, canonicalDatabase);
            previousTerm = entry.Term;
        }
    }
}
