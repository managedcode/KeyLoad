using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Replication;

/// <summary>Node-owned metadata persisted by the supplied checksummed atomic store; the caller owns that store.</summary>
public sealed class DurableReplicaLog : IDurableReplicaLog
{
    private const int ExclusiveProtocolPermit = 1;
    private const int BeforeFirstLogPosition = 0;
    private const int UnelectedTerm = 0;
    private const int FirstElectionTerm = 1;
    private const int EmptyEntryCount = 0;
    private const int FirstEntryIndex = 0;

    private readonly IAtomicStore store;
    private readonly ReplicaConfiguration configuration;
    private readonly Action<ReplicaCrashBoundary>? faultObserver;
    private readonly Lock gate = new();
    private ReplicaHardState state;
    private ReplicaTermObservation? termObservation;
    private bool disposed;

    /// <summary>Opens and validates node-owned durable state against the frozen topology-bound options.</summary>
    /// <param name="store">Borrowed atomic store holding a consistent fenced cut for every read.</param>
    /// <param name="configurationOptions">Centrally validated voter scope and bounded log persistence settings.</param>
    /// <param name="faultObserver">Optional observer invoked after durable crash boundaries.</param>
    /// <param name="canonicalDatabase">Borrowed canonical authority required for every nonnull native operation.</param>
    public DurableReplicaLog(IAtomicStore store, IOptions<ReplicaConfiguration> configurationOptions,
        Action<ReplicaCrashBoundary>? faultObserver = null, DatabaseEngine? canonicalDatabase = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(configurationOptions);
        configuration = configurationOptions.Value;
        configuration.Validate();
        this.store = store;
        this.faultObserver = faultObserver;
        CanonicalDatabase = canonicalDatabase;
        state = ReplicaLogValidation.Open(store, configuration, canonicalDatabase);
    }
    /// <inheritdoc />
    public SemaphoreSlim ProtocolGate { get; } = new(ExclusiveProtocolPermit, ExclusiveProtocolPermit);
    /// <summary>Gets the externally owned canonical engine used to validate operation authority and semantic retries.</summary>
    public DatabaseEngine? CanonicalDatabase { get; }
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
            return index <= (state.Snapshot?.Index ?? BeforeFirstLogPosition) || index > state.LastIndex ? null : Load(index);
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
        if (index == BeforeFirstLogPosition)
        { return UnelectedTerm; }
        if (state.Snapshot is { } snapshot && index == snapshot.Index)
        { return snapshot.Term; }
        if (index < (state.Snapshot?.Index ?? BeforeFirstLogPosition) || index > state.LastIndex || index < BeforeFirstLogPosition)
        {
            throw Errors.Fail(ErrorCode.NotFound, ReplicaPersistence.MissingEntry);
        }
        var result = ReplicaTermObservationReader.Read(store, index, state.Term, configuration.MaxAppendEntries, termObservation);
        termObservation = result.Observation;
        return result.Term;
    }

    private ReplicaEntry Load(long index)
    {
        var bytes = store.Read(view => view.ReadOwnedValue(ReplicaProtocol.EntryStorageKey(index)))
            ?? throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
        var entry = ReplicaProtocolCodec.DeserializeStored<ReplicaEntry>(bytes, configuration.MaxAppendEntries);
        if (entry.Index != index || entry.Term <= UnelectedTerm || entry.Term > state.Term)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
        }
        return ReplicaOperationAuthority.Own(entry, CanonicalDatabase);
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
            if (term < state.Term || term < FirstElectionTerm || votedFor is not null && !configuration.VoterIds.Contains(votedFor, StringComparer.Ordinal)
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
            if (entries.Count == EmptyEntryCount)
            { return; }
            var batch = ReplicaAppendCompiler.Validate(entries, state, configuration, Term, CanonicalDatabase);
            var next = ReplicaAppendCompiler.NextState(batch.Entries, state, Load, CanonicalDatabase);
            store.Commit((tx, _) =>
            {
                for (var position = FirstEntryIndex; position < batch.Entries.Length; position++)
                {
                    tx.Put(ReplicaProtocol.EntryStorageKey(batch.Entries[position].Index), batch.Encoded[position]);
                }
                tx.Put(ReplicaProtocol.StateStorageKey, ReplicaProtocolCodec.Serialize(next));
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
                Save(ReplicaCheckpointPublication.Next(snapshot, state, configuration, Term));
            }
        }
        finally { ProtocolGate.Release(); }
    }

    /// <inheritdoc />
    public bool HasCheckpointPrefix
    {
        get
        {
            lock (gate)
            {
                Check();
                return state.Snapshot is { } published && ReplicaPrefixReclaimer.HasPrefix(store, published, configuration);
            }
        }
    }

    /// <inheritdoc />
    public int ReclaimCheckpointPrefix(ReplicaSnapshot verifiedSnapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(verifiedSnapshot);
        ProtocolGate.Wait(cancellationToken);
        try
        {
            lock (gate)
            {
                Check();
                if (state.Snapshot != verifiedSnapshot || verifiedSnapshot.Index > state.CommittedIndex)
                { throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.InvalidSnapshot); }
                return ReplicaPrefixReclaimer.Reclaim(store, verifiedSnapshot, configuration, CanonicalDatabase, cancellationToken);
            }
        }
        finally { ProtocolGate.Release(); }
    }

    private void Save(ReplicaHardState next)
    {
        store.Commit((tx, _) => { tx.Put(ReplicaProtocol.StateStorageKey, ReplicaProtocolCodec.Serialize(next)); return true; });
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
