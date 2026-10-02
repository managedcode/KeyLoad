using KeyLoad.Storage;

namespace KeyLoad.Replication;

/// <summary>Bounded canonical checkpoint transfer; canonical storage and replica-log lifetime remain with the node host.</summary>
/// <param name="canonical">Borrowed canonical store whose snapshot format is verified before installation.</param>
/// <param name="log">Borrowed durable replica log publishing the verified snapshot cut.</param>
/// <param name="configuration">Snapshot scope, private directory and transfer size limits.</param>
/// <param name="faultObserver">Optional observer invoked at durable transfer and installation boundaries.</param>
public sealed class ReplicaSnapshotStore(IAtomicStore canonical, IDurableReplicaLog log, ReplicaConfiguration configuration,
    Action<ReplicaCrashBoundary>? faultObserver = null) : IReplicaSnapshotStore
{
    private readonly object gate = new();
    private readonly ReplicaSnapshotFiles files = new(configuration);
    /// <inheritdoc />
    public ReplicaSnapshot? Current => log.State.Snapshot;
    private ReplicaIncomingTransfer Incoming => new(files, configuration, faultObserver);

    /// <inheritdoc />
    public void Recover()
    {
        lock (gate)
        {
            ValidateScope();
            if (Current is { } published)
            {
                ReplicaPersistence.ValidateSnapshot(published, configuration);
                ReplicaPersistence.VerifyImage(canonical, files.ImagePath(published), published);
            }
            Incoming.Recover(canonical, Current, Complete);
            var applied = ReplicaPersistence.AppliedPosition(canonical);
            if (applied > log.State.CommittedIndex || applied < (Current?.Index ?? 0))
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaPersistence.CanonicalAhead);
            }
        }
    }

    /// <inheritdoc />
    public void ResetIncoming()
    {
        lock (gate)
        {
            Recover();
            files.ClearIncoming();
        }
    }

    /// <inheritdoc />
    public ReplicaSnapshot Create(long index, long term)
    {
        lock (gate)
        {
            ValidateScope();
            if (index <= 0 || index > log.State.CommittedIndex || log.TermAt(index) != term)
            {
                throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.InvalidSnapshot);
            }
            var transferId = Guid.NewGuid();
            var candidate = new ReplicaSnapshot(transferId, configuration.Incarnation, index, term, 1,
                string.Empty, transferId.ToString(ReplicaPersistence.GuidFormat) + ReplicaProtocol.SnapshotExtension);
            var temporary = files.TemporaryPath(candidate);
            try
            {
                canonical.CreateSnapshot(temporary, index);
                var snapshot = files.Describe(temporary, transferId, index, term);
                ReplicaPersistence.ValidateSnapshot(snapshot, configuration);
                ReplicaPersistence.VerifyImage(canonical, temporary, snapshot);
                using (var image = ReplicaSnapshotFiles.OpenPrivate(temporary, FileMode.Open))
                { image.Flush(true); }
                File.Move(temporary, files.ImagePath(snapshot));
                log.PublishSnapshot(snapshot);
                return snapshot;
            }
            finally { File.Delete(temporary); }
        }
    }

    /// <inheritdoc />
    public long Begin(ReplicaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (gate)
        {
            ValidateScope();
            ReplicaPersistence.ValidateSnapshot(snapshot, configuration);
            if (Current == snapshot)
            { return snapshot.Length; }
            ValidateCut(snapshot);
            var offset = Incoming.Begin(snapshot);
            faultObserver?.Invoke(ReplicaCrashBoundary.SnapshotTransferBegun);
            return offset;
        }
    }

    /// <inheritdoc />
    public long Append(Guid transferId, long offset, ReadOnlySpan<byte> bytes)
    {
        lock (gate)
        {
            ValidateScope();
            var acknowledged = Incoming.Append(transferId, offset, bytes);
            faultObserver?.Invoke(ReplicaCrashBoundary.SnapshotChunkAcknowledged);
            return acknowledged;
        }
    }

    /// <inheritdoc />
    public ReplicaSnapshot Complete(Guid transferId)
    {
        lock (gate)
        {
            ValidateScope();
            if (Current is { } current && current.TransferId == transferId && Incoming.Descriptor is null)
            { return current; }
            var snapshot = Incoming.Required(transferId);
            ValidateCut(snapshot);
            if (Incoming.Length(snapshot) != snapshot.Length)
            {
                throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.SnapshotUnavailable);
            }
            VerifyIncoming(snapshot);
            faultObserver?.Invoke(ReplicaCrashBoundary.SnapshotVerified);
            if (ReplicaPersistence.AppliedPosition(canonical) < snapshot.Index)
            {
                canonical.InstallSnapshot(files.TransferImage(snapshot), snapshot.Index);
            }
            faultObserver?.Invoke(ReplicaCrashBoundary.SnapshotInstalled);
            files.PublishImage(snapshot);
            log.PublishSnapshot(snapshot);
            files.ClearIncoming();
            faultObserver?.Invoke(ReplicaCrashBoundary.SnapshotPublished);
            return snapshot;
        }
    }

    private void VerifyIncoming(ReplicaSnapshot snapshot)
    {
        try
        { ReplicaPersistence.VerifyImage(canonical, files.TransferImage(snapshot), snapshot); }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Corruption or ErrorCode.FormatUnsupported or ErrorCode.Validation)
        {
            faultObserver?.Invoke(ReplicaCrashBoundary.SnapshotRejected);
            files.DiscardIncoming(snapshot, Current);
            throw;
        }
    }

    private void ValidateCut(ReplicaSnapshot snapshot)
    {
        var state = log.State;
        if (snapshot.Index < ReplicaPersistence.AppliedPosition(canonical) || snapshot.Index < (state.Snapshot?.Index ?? 0)
            || snapshot.Index <= state.CommittedIndex && log.TermAt(snapshot.Index) != snapshot.Term)
        {
            throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.InvalidSnapshot);
        }
    }

    private void ValidateScope()
    {
        if (canonical.Identity.Incarnation != configuration.Incarnation || log.State.Incarnation != configuration.Incarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ReplicaProtocol.InvalidSnapshot);
        }
    }

    /// <inheritdoc />
    public byte[] ReadChunk(Guid transferId, long offset, int maxBytes)
    {
        lock (gate)
        {
            ValidateScope();
            var snapshot = Current;
            if (snapshot is null || snapshot.TransferId != transferId)
            {
                throw Errors.Fail(ErrorCode.NotFound, ReplicaProtocol.SnapshotUnavailable);
            }
            if (offset < 0 || offset > snapshot.Length || maxBytes < 1 || maxBytes > configuration.SnapshotChunkBytes)
            {
                throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidSnapshot);
            }
            using var image = File.OpenRead(files.ImagePath(snapshot));
            if (image.Length != snapshot.Length)
            { throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidSnapshot); }
            var bytes = new byte[(int)Math.Min(maxBytes, snapshot.Length - offset)];
            image.Position = offset;
            image.ReadExactly(bytes);
            return bytes;
        }
    }
}
