using System.Threading.Channels;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Replication;

/// <summary>Node-owned ordered apply worker; protocol RPCs do not await canonical snapshot IO under their term gate.</summary>
public sealed class ReplicaMaterializer : IAsyncDisposable
{
    private readonly int applyBatchSize;
    private readonly Channel<bool> work = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly SemaphoreSlim applyGate = new(1, 1);
    private readonly Lock signals = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task worker;
    private Task? shutdown;
    private TaskCompletionSource changed = NewSignal();
    private Exception? failure;

    /// <summary>Constructs and recovers the committed prefix before exposing a node-owned worker.</summary>
    /// <param name="database">Borrowed canonical engine owned by the physical partition host.</param>
    /// <param name="log">Borrowed durable log supplying the committed prefix.</param>
    /// <param name="snapshots">Borrowed checkpoint store that recovers verified pending installations.</param>
    /// <param name="options">Centrally validated ordered apply budgets, frozen for this physical owner.</param>
    public ReplicaMaterializer(DatabaseEngine database, IDurableReplicaLog log, IReplicaSnapshotStore snapshots,
        IOptions<ReplicaExecutionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Value;
        settings.Validate();
        applyBatchSize = settings.ApplyBatchSize;
        Database = database;
        Log = log;
        Snapshots = snapshots;
        Recover();
        worker = Task.Run(ApplyWorkerAsync);
        work.Writer.TryWrite(true);
    }

    /// <summary>The canonical engine stays owned by the physical node.</summary>
    public DatabaseEngine Database { get; }
    /// <summary>The node's durable replica metadata and entries.</summary>
    public IDurableReplicaLog Log { get; }
    /// <summary>The node's verified checkpoint transfer store.</summary>
    public IReplicaSnapshotStore Snapshots { get; }
    internal SemaphoreSlim ProtocolGate => Log.ProtocolGate;

    /// <summary>Returns the current canonical cut under the same registration lock used by applied-position waiters.</summary>
    public long AppliedPosition
    {
        get
        {
            lock (signals)
            {
                Check();
                return Database.LastApplied;
            }
        }
    }

    /// <summary>Rejects unsupported canonical cuts and completes verified interrupted snapshot installs.</summary>
    public void Recover()
    {
        Snapshots.Recover();
        if (Database.LastApplied > Log.State.CommittedIndex)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaProtocol.CorruptLog);
        }
    }

    /// <summary>Flushes the commit cut before waking ordered canonical materialization.</summary>
    /// <param name="index">Monotonic replicated index to publish as committed.</param>
    public void Commit(long index)
    {
        Check();
        Log.Commit(index);
        work.Writer.TryWrite(true);
    }

    /// <summary>Waits for the canonical cut, propagating storage failures instead of an unbounded silent wait.</summary>
    /// <param name="index">Committed index that must be applied before completion.</param>
    /// <param name="cancellationToken">Cancellation of the wait for the apply cut.</param>
    /// <returns>Completion once the canonical engine reaches the requested cut.</returns>
    public async Task WaitForApplyAsync(long index, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        while (true)
        {
            Task signal;
            lock (signals)
            {
                Check();
                if (Database.LastApplied >= index)
                {
                    return;
                }
                signal = changed.Task;
            }
            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Waits until canonical apply changes from the caller's observed cut.</summary>
    /// <param name="observedPosition">The cut observed before registering the wait.</param>
    /// <param name="cancellationToken">Cancellation of this caller's wait only.</param>
    /// <returns>The changed canonical cut.</returns>
    public async Task<long> WaitForAppliedPositionChangeAsync(long observedPosition, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task signal;
            lock (signals)
            {
                Check();
                var current = Database.LastApplied;
                if (current != observedPosition)
                {
                    return current;
                }
                signal = changed.Task;
            }
            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyWorkerAsync()
    {
        try
        {
            await foreach (var _ in work.Reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
            {
                await applyGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                try
                { ReplicaCanonicalApply.ApplyBatch(Database, Log, applyBatchSize); }
                finally { applyGate.Release(); }
                PublishChange();
                if (Database.LastApplied < Log.State.CommittedIndex)
                {
                    work.Writer.TryWrite(true);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is KeyLoadException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            lock (signals)
            { failure = error; }
        }
        catch (Exception error)
        {
            lock (signals)
            { failure = error; }
            throw;
        }
        finally { PublishChange(); }
    }

    /// <summary>Fences any verified recovery during replacement of an old leader's incomplete upload.</summary>
    /// <param name="snapshot">Authenticated leader descriptor for the incoming verified checkpoint.</param>
    /// <param name="cancellationToken">Cancellation while waiting for canonical apply ownership.</param>
    /// <returns>The durable incoming offset for contiguous transfer resumption.</returns>
    public async Task<long> BeginCheckpointAsync(ReplicaSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Check();
            return await Task.Run(() => ReplicaCanonicalApply.Begin(Snapshots, snapshot), cancellationToken).ConfigureAwait(false);
        }
        finally { applyGate.Release(); PublishChange(); }
    }

    /// <summary>Captures the exact applied cut without holding the protocol term gate during file IO.</summary>
    /// <param name="cancellationToken">Cancellation while waiting for checkpoint capture ownership.</param>
    /// <returns>The published verified checkpoint, or null before a positive applied cut.</returns>
    public async Task<ReplicaSnapshot?> CreateCheckpointAsync(CancellationToken cancellationToken)
    {
        await applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Check();
            var cut = Database.LastApplied;
            if (cut < 1 || cut <= (Snapshots.Current?.Index ?? 0))
            {
                return Snapshots.Current;
            }
            return await Task.Run(() => Snapshots.Create(cut, Log.TermAt(cut)), cancellationToken).ConfigureAwait(false);
        }
        finally { applyGate.Release(); }
    }

    /// <summary>Installs only a fully verified image while fencing the canonical apply worker.</summary>
    /// <param name="transferId">Incoming transfer whose full image is ready for verification.</param>
    /// <param name="cancellationToken">Cancellation while waiting for installation ownership.</param>
    /// <returns>The installed and durably published checkpoint descriptor.</returns>
    public async Task<ReplicaSnapshot> InstallCheckpointAsync(Guid transferId, CancellationToken cancellationToken)
    {
        await applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        ReplicaSnapshot snapshot;
        try
        {
            Check();
            snapshot = await Task.Run(() => Snapshots.Complete(transferId), cancellationToken).ConfigureAwait(false);
            work.Writer.TryWrite(true);
        }
        finally { applyGate.Release(); }
        PublishChange();
        return snapshot;
    }

    private void Check()
    {
        if (Volatile.Read(ref failure) is not null || lifetime.IsCancellationRequested || worker is { IsFaulted: true })
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaProtocol.CorruptLog);
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private void PublishChange()
    {
        lock (signals)
        {
            var previous = changed;
            changed = NewSignal();
            previous.TrySetResult();
        }
    }

    /// <summary>Stops the node-owned apply loop before its independently owned stores are disposed.</summary>
    /// <returns>Completion after the apply worker and its owned gate drain; the borrowed protocol gate remains with the log owner.</returns>
    public ValueTask DisposeAsync()
    {
        lock (signals)
        {
            shutdown ??= ReplicaMaterializerShutdown.DisposeAsync(lifetime, work.Writer, worker, applyGate, PublishChange);
            return new(shutdown);
        }
    }
}
