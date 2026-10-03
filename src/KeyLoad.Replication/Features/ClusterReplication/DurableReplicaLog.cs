using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

/// <summary>Node-owned metadata persisted by the supplied checksummed atomic store; the caller owns that store.</summary>
/// <param name="store">Node-owned store that durably commits replica entries and hard state. Its Read must check
/// poison/disposal and hold a consistent cut. Position and Identity.NodeId, Incarnation and ReadGeneration inside the
/// callback must fence writes, replacement and restore as specified by ADR-061.</param>
/// <param name="configuration">Fixed voter scope and bounded log persistence settings.</param>
/// <param name="faultObserver">Optional observer invoked after durable crash boundaries.</param>
public sealed class DurableReplicaLog(IAtomicStore store, ReplicaConfiguration configuration,
    Action<ReplicaCrashBoundary>? faultObserver = null) : IDurableReplicaLog
{
    private readonly object gate = new();
    private ReplicaHardState state = ReplicaLogValidation.Open(store, configuration);
    private ReplicaTermObservation? termObservation;
    private bool disposed;
    /// <inheritdoc />
    public SemaphoreSlim ProtocolGate { get; } = new(1, 1);
    /// <inheritdoc />
    public ReplicaHardState State
    {
        get { lock (gate) { Check(); return state; } }
    }

    /// <inheritdoc />
    public ReplicaEntry? ReadEntry(long index)
    {
        lock (gate)
        {
            Check();
            return index <= (state.Snapshot?.Index ?? 0) || index > state.LastIndex ? null : Load(index);
        }
    }

    /// <inheritdoc />
    public long TermAt(long index)
    {
        lock (gate)
        {
            Check();
            return Term(index);
        }
    }

    private long Term(long index)
    {
        if (index == 0)
        { return 0; }
        if (state.Snapshot is { } snapshot && index == snapshot.Index)
        { return snapshot.Term; }
        if (index < (state.Snapshot?.Index ?? 0) || index > state.LastIndex || index < 0)
        {
            throw Errors.Fail(ErrorCode.NotFound, ReplicaPersistence.MissingEntry);
        }
        var result = ReplicaTermObservationReader.Read(store, index, state.Term, termObservation);
        termObservation = result.Observation;
        return result.Term;
    }

    private ReplicaEntry Load(long index)
    {
        var bytes = store.Read(view => view.ReadOwnedValue(ReplicaProtocol.EntryStorageKey(index)))
            ?? throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
        var entry = ReplicaProtocolCodec.Deserialize<ReplicaEntry>(bytes);
        if (entry.Index != index || entry.Term <= 0 || entry.Term > state.Term)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
        }
        return entry;
    }

    /// <inheritdoc />
    public ImmutableArray<ReplicaEntry> Read(long firstIndex, int maxEntries, int maxBytes)
    {
        lock (gate)
        {
            Check();
            return ReplicaLogReader.Read(firstIndex, maxEntries, maxBytes, configuration, state, Load);
        }
    }

    /// <inheritdoc />
    public void SaveTermAndVote(long term, string? votedFor)
    {
        lock (gate)
        {
            Check();
            if (term < state.Term || term < 1 || votedFor is not null && !configuration.VoterIds.Contains(votedFor, StringComparer.Ordinal)
                || term == state.Term && state.VotedFor is not null && votedFor is not null && state.VotedFor != votedFor)
            {
                throw Errors.Fail(ErrorCode.Conflict, ReplicaPersistence.InvalidVote);
            }
            var vote = term == state.Term ? state.VotedFor ?? votedFor : votedFor;
            Save(state with { Term = term, VotedFor = vote });
            faultObserver?.Invoke(ReplicaCrashBoundary.TermSaved);
        }
    }

    /// <inheritdoc />
    public void Append(IReadOnlyList<ReplicaEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        lock (gate)
        {
            Check();
            if (entries.Count == 0)
            { return; }
            var encoded = ReplicaAppendCompiler.Validate(entries, state, configuration, Term);
            var next = ReplicaAppendCompiler.NextState(entries, encoded, state, Load);
            store.Commit((tx, _) =>
            {
                for (var position = 0; position < entries.Count; position++)
                {
                    tx.Put(ReplicaProtocol.EntryStorageKey(entries[position].Index), encoded[position]);
                }
                tx.PutRecord(ReplicaProtocol.StateStorageKey, next);
                return true;
            });
            state = next;
            faultObserver?.Invoke(ReplicaCrashBoundary.EntryAcknowledged);
        }
    }

    /// <inheritdoc />
    public void Commit(long index)
    {
        lock (gate)
        {
            Check();
            if (index < state.CommittedIndex || index > state.LastIndex)
            {
                throw Errors.Fail(ErrorCode.Conflict, ReplicaPersistence.InvalidCommit);
            }
            Save(state with { CommittedIndex = index });
            faultObserver?.Invoke(ReplicaCrashBoundary.CommitAcknowledged);
        }
    }

    /// <inheritdoc />
    public void PublishSnapshot(ReplicaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProtocolGate.Wait();
        try
        {
            lock (gate)
            {
                Check();
                ReplicaPersistence.ValidateSnapshot(snapshot, configuration);
                if (snapshot.Index < (state.Snapshot?.Index ?? 0))
                {
                    throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.InvalidSnapshot);
                }
                var retain = snapshot.Index <= state.LastIndex && Term(snapshot.Index) == snapshot.Term;
                if (!retain && snapshot.Index <= state.CommittedIndex)
                {
                    throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.InvalidSnapshot);
                }
                Save(state with
                {
                    Snapshot = snapshot,
                    LastIndex = retain ? state.LastIndex : snapshot.Index,
                    CommittedIndex = Math.Max(state.CommittedIndex, snapshot.Index),
                    Term = Math.Max(state.Term, snapshot.Term),
                    VotedFor = snapshot.Term > state.Term ? null : state.VotedFor
                });
            }
        }
        finally { ProtocolGate.Release(); }
    }

    private void Save(ReplicaHardState next)
    {
        store.Commit((tx, _) => { tx.PutRecord(ReplicaProtocol.StateStorageKey, next); return true; });
        state = next;
    }

    private void Check() => ObjectDisposedException.ThrowIf(disposed, this);
    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            ProtocolGate.Dispose();
        }
    }
}
