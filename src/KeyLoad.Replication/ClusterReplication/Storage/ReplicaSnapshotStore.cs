using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Replication;

/// <summary>Bounded canonical checkpoint transfer; canonical storage and replica-log lifetime remain with the node host.</summary>
public sealed class ReplicaSnapshotStore : IReplicaSnapshotStore
{
    private const int BeforeFirstLogPosition = 0;
    private const int PendingSnapshotLength = 1;
    private const int BeforeFirstTransferByte = 0;
    private const int MinimumChunkLength = 1;

    private readonly IAtomicStore canonical;
    private readonly IDurableReplicaLog log;
    private readonly ReplicaConfiguration configuration;
    private readonly Action<ReplicaCrashBoundary>? faultObserver;
    private readonly Action<CancellationToken> beforeReplacement;
    private readonly Lock gate = new();
    private readonly ReplicaSnapshotFiles files;
    private readonly ReplicaIncomingTransfer incoming;

    /// <summary>Creates the bounded snapshot owner from one validated topology-bound configuration snapshot.</summary>
    /// <param name="canonical">Borrowed canonical store whose snapshot format is verified before installation.</param>
    /// <param name="log">Borrowed durable replica log publishing the verified snapshot cut.</param>
    /// <param name="configurationOptions">Centrally validated snapshot scope, private directory and transfer limits.</param>
    /// <param name="executionOptions">Centrally validated replica IO budgets.</param>
    /// <param name="beforeReplacement">Mandatory current live-state guard invoked only while the caller owns exclusive replacement.</param>
    /// <param name="faultObserver">Optional observer invoked at durable transfer and installation boundaries.</param>
    public ReplicaSnapshotStore(IAtomicStore canonical, IDurableReplicaLog log, IOptions<ReplicaConfiguration> configurationOptions,
        IOptions<ReplicaExecutionOptions> executionOptions, Action<CancellationToken> beforeReplacement,
        Action<ReplicaCrashBoundary>? faultObserver = null)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(beforeReplacement);
        ArgumentNullException.ThrowIfNull(configurationOptions);
        configuration = configurationOptions.Value;
        configuration.Validate();
        this.canonical = canonical;
        this.log = log;
        this.faultObserver = faultObserver;
        this.beforeReplacement = beforeReplacement;
        files = new(configurationOptions, executionOptions);
        incoming = new(files, configurationOptions, faultObserver);
    }
    /// <inheritdoc />
    public ReplicaSnapshot? Current => log.State.Snapshot;
    /// <inheritdoc />
    public int ReclaimCheckpointPrefix(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            ValidateScope();
            return ReplicaVerifiedPrefixReclamation.Reclaim(canonical, log, configuration, files, cancellationToken);
        }
    }

    private ReplicaIncomingTransfer Incoming => incoming;

    /// <inheritdoc />
    public void Recover() => Recover(default);

    /// <inheritdoc />
    public void Recover(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            ValidateScope();
            if (Current is { } published)
            {
                ReplicaPersistence.ValidateSnapshot(published, configuration);
                ReplicaPersistence.VerifyImage(canonical, files.ImagePath(published), published);
            }
            Incoming.Recover(canonical, Current, transferId => Complete(transferId, cancellationToken));
            var applied = ReplicaPersistence.AppliedPosition(canonical);
            if (applied > log.State.CommittedIndex || applied < (Current?.Index ?? BeforeFirstLogPosition))
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaPersistence.CanonicalAhead);
            }
        }
    }

    /// <inheritdoc />
    public void ResetIncoming() => ResetIncoming(default);

    /// <inheritdoc />
    public void ResetIncoming(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            Recover(cancellationToken);
            files.ClearIncoming();
        }
    }

    /// <inheritdoc />
    public ReplicaSnapshot Create(long index, long term)
    {
        lock (gate)
        {
            ValidateScope();
            if (index <= BeforeFirstLogPosition || index > log.State.CommittedIndex || log.TermAt(index) != term)
            {
                throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.InvalidSnapshot);
            }
            var transferId = Guid.NewGuid();
            var candidate = new ReplicaSnapshot(transferId, configuration.Incarnation, index, term, PendingSnapshotLength,
                string.Empty, transferId.ToString(ReplicaPersistence.GuidFormat) + ReplicaProtocol.SnapshotExtension);
            var temporary = files.TemporaryPath(candidate);
            try
            {
                canonical.CreateSnapshot(temporary, index);
                var snapshot = files.Describe(temporary, transferId, index, term);
                ReplicaPersistence.ValidateSnapshot(snapshot, configuration);
                ReplicaPersistence.VerifyImage(canonical, temporary, snapshot);
                using (var image = files.OpenPrivate(temporary, FileMode.Open))
                {
                    image.Flush(true);
                }
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
            {
                return snapshot.Length;
            }
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
    public ReplicaSnapshot Complete(Guid transferId) => Complete(transferId, default);

    /// <inheritdoc />
    public ReplicaSnapshot Complete(Guid transferId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            ValidateScope();
            if (Current is { } current && current.TransferId == transferId && Incoming.Descriptor is null)
            {
                return current;
            }
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
                beforeReplacement(cancellationToken);
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
        {
            ReplicaPersistence.VerifyImage(canonical, files.TransferImage(snapshot), snapshot);
        }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Corruption or ErrorCode.FormatUnsupported or ErrorCode.Validation)
        {
            faultObserver?.Invoke(ReplicaCrashBoundary.SnapshotRejected);
            files.DiscardIncoming(snapshot, Current);
            throw;
        }
    }

    private void ValidateCut(ReplicaSnapshot snapshot)
        => ReplicaSnapshotAdmission.ValidateCut(canonical, log, snapshot);

    private void ValidateScope()
        => ReplicaSnapshotAdmission.ValidateScope(canonical, log, configuration);

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
            if (offset < BeforeFirstTransferByte || offset > snapshot.Length || maxBytes < MinimumChunkLength || maxBytes > configuration.SnapshotChunkBytes)
            {
                throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidSnapshot);
            }
            using var image = files.OpenPublishedRead(snapshot);
            if (image.Length != snapshot.Length)
            {
                throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidSnapshot);
            }
            var bytes = new byte[(int)Math.Min(maxBytes, snapshot.Length - offset)];
            image.Position = offset;
            image.ReadExactly(bytes);
            return bytes;
        }
    }
}
