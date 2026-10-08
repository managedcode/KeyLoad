using System.Threading.Channels;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Replication;

/// <summary>Node-owned ordered apply worker; protocol RPCs do not await canonical snapshot IO under their term gate.</summary>
public sealed class ReplicaMaterializer : IAsyncDisposable
{
    private const int ExclusiveApplyPermit = 1;
    private const int BeforeFirstLogPosition = 0;

    private readonly int applyBatchSize;
    private readonly Func<ReplicaEntry, IDisposable?>? canonicalObservation;
    private const int CoalescedApplyWakeCapacity = 1;
    private readonly Channel<bool> work = Channel.CreateBounded<bool>(new BoundedChannelOptions(CoalescedApplyWakeCapacity)
    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly SemaphoreSlim applyGate = new(ExclusiveApplyPermit, ExclusiveApplyPermit);
    private readonly Lock signals = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task worker;
    private readonly ReplicaMaterializerAppliedPositionSignals appliedPosition;
    private Task? shutdown;
    private Exception? failure;

    /// <summary>Constructs and recovers the committed prefix before exposing a node-owned worker.</summary>
    /// <param name="database">Borrowed canonical engine owned by the physical partition host.</param>
    /// <param name="log">Borrowed durable log supplying the committed prefix.</param>
    /// <param name="snapshots">Borrowed checkpoint store that recovers verified pending installations.</param>
    /// <param name="options">Centrally validated ordered apply budgets, frozen for this physical owner.</param>
    /// <param name="canonicalObservation">Optional closed private native apply observation, opened on this worker only.</param>
    public ReplicaMaterializer(DatabaseEngine database, IDurableReplicaLog log, IReplicaSnapshotStore snapshots,
        IOptions<ReplicaExecutionOptions> options, Func<ReplicaEntry, IDisposable?>? canonicalObservation = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Value;
        settings.Validate();
        applyBatchSize = settings.ApplyBatchSize;
        this.canonicalObservation = canonicalObservation;
        Database = database;
        Log = log;
        Snapshots = snapshots;
        appliedPosition = new(signals, database, Check);
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
        applyGate.Wait();
        long recoveredCut;
        try
        {
            Snapshots.Recover();
            if (Database.LastApplied > Log.State.CommittedIndex)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaProtocol.CorruptLog);
            }
            recoveredCut = Database.LastApplied;
        }
        finally { applyGate.Release(); }
        appliedPosition.PublishCompletedCut(recoveredCut);
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
    public Task WaitForApplyAsync(long index, CancellationToken cancellationToken)
        => appliedPosition.WaitForApplyAsync(index, cancellationToken);

    /// <summary>Waits until a completed canonical cut advances beyond the caller's observed position.</summary>
    /// <param name="observedPosition">The cut observed before registering the wait.</param>
    /// <param name="cancellationToken">Cancellation of this caller's wait only.</param>
    /// <returns>The changed canonical cut.</returns>
    public Task<long> WaitForAppliedPositionChangeAsync(long observedPosition, CancellationToken cancellationToken)
        => appliedPosition.WaitForChangeAsync(observedPosition, cancellationToken);

    private async Task ApplyWorkerAsync()
    {
        try
        {
            await foreach (var _ in work.Reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
            {
                await applyGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                long completedCut = BeforeFirstLogPosition;
                try
                {
                    ReplicaCanonicalApply.ApplyBatch(Database, Log, applyBatchSize, canonicalObservation);
                    completedCut = Database.LastApplied;
                }
                finally { applyGate.Release(); }
                appliedPosition.PublishCompletedCut(completedCut);
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
            throw;
        }
        catch (Exception error)
        {
            lock (signals)
            { failure = error; }
            throw;
        }
        finally { appliedPosition.WakeWaiters(); }
    }

    /// <summary>Fences any verified recovery during replacement of an old leader's incomplete upload.</summary>
    /// <param name="snapshot">Authenticated leader descriptor for the incoming verified checkpoint.</param>
    /// <param name="cancellationToken">Cancellation while waiting for canonical apply ownership.</param>
    /// <returns>The durable incoming offset for contiguous transfer resumption.</returns>
    public async Task<long> BeginCheckpointAsync(ReplicaSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        long completedCut = BeforeFirstLogPosition;
        var completed = false;
        try
        {
            Check();
            var offset = await Task.Run(() => ReplicaCanonicalApply.Begin(Snapshots, snapshot), cancellationToken)
                .ConfigureAwait(false);
            completedCut = Database.LastApplied;
            completed = true;
            return offset;
        }
        finally
        {
            applyGate.Release();
            if (completed)
            { appliedPosition.PublishCompletedCut(completedCut); }
            else
            { appliedPosition.WakeWaiters(); }
        }
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
            return await Task.Run(() => ReplicaCheckpointReclamation.CaptureAndReclaim(Database,
                Log, Snapshots, cancellationToken), cancellationToken).ConfigureAwait(false);
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
        long installedCut = BeforeFirstLogPosition;
        try
        {
            Check();
            snapshot = await Task.Run(() => Snapshots.Complete(transferId), cancellationToken).ConfigureAwait(false);
            installedCut = Database.LastApplied;
            work.Writer.TryWrite(true);
        }
        finally { applyGate.Release(); }
        appliedPosition.PublishCompletedCut(installedCut);
        return snapshot;
    }

    private void Check()
    {
        if (Volatile.Read(ref failure) is not null || lifetime.IsCancellationRequested || worker is { IsFaulted: true })
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaProtocol.CorruptLog);
        }
    }

    /// <summary>Stops the node-owned apply loop before its independently owned stores are disposed.</summary>
    /// <returns>Completion after the apply worker and its owned gate drain; the borrowed protocol gate remains with the log owner.</returns>
    public ValueTask DisposeAsync()
    {
        lock (signals)
        {
            shutdown ??= ReplicaMaterializerShutdown.DisposeAsync(lifetime, work.Writer, worker, applyGate,
                appliedPosition.WakeWaiters);
            return new(shutdown);
        }
    }
}

/// <summary>Coalesces published canonical cuts under the materializer's existing waiter-registration lock.</summary>
internal sealed class ReplicaMaterializerAppliedPositionSignals
{
    private readonly Lock signals;
    private readonly DatabaseEngine database;
    private readonly Action check;
    private TaskCompletionSource changed = NewSignal();
    private long publishedAppliedPosition;

    internal ReplicaMaterializerAppliedPositionSignals(Lock signals, DatabaseEngine database, Action check)
    {
        this.signals = signals ?? throw new ArgumentNullException(nameof(signals));
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(check);
        this.database = database;
        this.check = check;
    }

    internal async Task WaitForApplyAsync(long index, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        while (true)
        {
            Task signal;
            lock (signals)
            {
                check();
                if (database.LastApplied >= index)
                { return; }
                signal = changed.Task;
            }
            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task<long> WaitForChangeAsync(long observedPosition, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task signal;
            lock (signals)
            {
                check();
                if (publishedAppliedPosition > observedPosition)
                { return publishedAppliedPosition; }
                signal = changed.Task;
            }
            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal void PublishCompletedCut(long completedCut)
    {
        lock (signals)
        {
            if (completedCut <= publishedAppliedPosition)
            { return; }
            publishedAppliedPosition = completedCut;
            RotateSignal();
        }
    }

    internal void WakeWaiters()
    {
        lock (signals)
        { RotateSignal(); }
    }

    private void RotateSignal()
    {
        var previous = changed;
        changed = NewSignal();
        previous.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
