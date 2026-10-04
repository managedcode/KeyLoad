using System.Text;
using KeyLoad.Core;
using Microsoft.Extensions.Hosting;

namespace KeyLoad.Replication;

/// <summary>Bounded node admission and stable-ID outcomes over the Orleans replica protocol.</summary>
public sealed class ClusterCoordinator : ICommitCoordinator, IHostedService, IAsyncDisposable
{
    private readonly ReplicaConsensus consensus;
    private readonly DatabaseEngine database;
    private readonly TimeProvider clock;
    private readonly AdmittedCommandInbox commands;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object lifecycle = new();
    private Task? worker;
    private Task? shutdown;
    private int disposed;

    /// <summary>Creates admission without starting transport or waiting for membership.</summary>
    /// <param name="consensus">Physical node protocol whose wire transport is supplied by Orleans.</param>
    /// <param name="database">Node-owned canonical engine and persisted admission principals.</param>
    /// <param name="admission">Configured independent control and data admission budgets.</param>
    /// <param name="clock">Node system clock; a caller cannot choose leader evaluation time.</param>
    public ClusterCoordinator(ReplicaConsensus consensus, DatabaseEngine database,
        CommandAdmissionGovernor admission, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(consensus);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(clock);
        this.consensus = consensus;
        this.database = database;
        this.clock = clock;
        database.Durability = DurabilityProfile.QuorumProcessDurable;
        commands = new(admission);
        consensus.ConfigureForwarding(AcceptForwardedAsync);
    }

    /// <inheritdoc />
    public Task<OperationResult> SubmitAsync(OperationKind kind, Guid id, string principalId, string payloadJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payloadJson);
        return AdmitAsync(database.NormalizeOperation(new(id, kind, principalId, clock.GetUtcNow(), payloadJson)),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult> SubmitNativeAsync(OperationKind kind, Guid id, string principalId,
        ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        => AdmitAsync(database.CreateNativeOperation(kind, id, principalId, clock.GetUtcNow(), payload), cancellationToken);

    private async Task<OperationResult> AdmitAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation.PrincipalId);
        cancellationToken.ThrowIfCancellationRequested();
        if (worker is null || worker.IsCompleted)
        { throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader); }
        var bytes = Math.Max(Encoding.UTF8.GetByteCount(operation.PayloadJson), operation.NativePayload.Length);
        if (operation.Id == Guid.Empty || bytes > database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaProtocol.InvalidAppend); }
        var evaluated = operation with { EvaluatedAt = clock.GetUtcNow() };
        var principal = database.Store.Read(view => database.Principal(view, evaluated.PrincipalId, evaluated.EvaluatedAt));
        var pending = commands.Enqueue(evaluated, principal, bytes, cancellationToken);
        try
        { return await pending.Completion.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, ReplicaProtocol.InterruptedWrite); }
    }

    /// <summary>Admits an authenticated forwarded operation only on a ready current leader.</summary>
    /// <param name="operation">Stable-ID operation forwarded by an authenticated replica.</param>
    /// <param name="cancellationToken">Cancellation while checking leadership and awaiting admission.</param>
    /// <returns>The persisted operation outcome after bounded leader admission.</returns>
    public async Task<OperationResult> AcceptForwardedAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!await consensus.IsLeaderAsync(cancellationToken).ConfigureAwait(false))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader);
        }

        return await AdmitAsync(database.NormalizeOperation(operation), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReadBarrierAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await consensus.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader);
        }
    }

    /// <summary>Starts admission before Orleans membership submits its first control write.</summary>
    /// <param name="cancellationToken">Startup cancellation before publishing the admission worker.</param>
    /// <returns>Completion once the node-owned admission worker is scheduled.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (lifecycle)
        {
            if (worker is not null || shutdown is not null)
            {
                throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.InvalidPeer);
            }

            worker = Task.Run(() => RunAsync(lifetime.Token), CancellationToken.None);
        }
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await commands.ReadAsync(stoppingToken).ConfigureAwait(false) is { } pending)
            {
                await ExecuteAsync(pending, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { commands.Stop(); }
    }

    private async Task ExecuteAsync(AdmittedCommand pending, CancellationToken stoppingToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        deadline.CancelAfter(ReplicaProtocol.CommandTimeout);
        try
        {
            pending.Complete(await consensus.SubmitAsync(pending.Operation, deadline.Token).ConfigureAwait(false));
        }
        catch (KeyLoadException error) { pending.Fail(error); }
        catch (OperationCanceledException)
        {
            pending.Fail(Errors.Fail(ErrorCode.UnknownWriteOutcome, ReplicaProtocol.InterruptedWrite));
        }
        catch (Exception)
        {
            pending.Fail(Errors.Fail(ErrorCode.UnknownWriteOutcome, ReplicaProtocol.InterruptedWrite));
            throw;
        }
    }

    /// <summary>Stops admission after Orleans membership shutdown, with idempotent draining.</summary>
    /// <param name="cancellationToken">Cancellation of the caller's wait for shared admission shutdown.</param>
    /// <returns>Completion after the admitted queue and worker drain.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            shutdown ??= StopCoreAsync();
            return shutdown.WaitAsync(cancellationToken);
        }
    }

    private async Task StopCoreAsync()
    {
        commands.Stop();
        await lifetime.CancelAsync().ConfigureAwait(false);
        if (worker is not null)
        { await worker.ConfigureAwait(false); }
    }

    /// <summary>Drains the queue once; DI aliases do not dispose its lifetime source twice.</summary>
    /// <returns>Completion after shared shutdown and admission resource disposal.</returns>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await commands.DisposeAsync().ConfigureAwait(false);
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            { lifetime.Dispose(); }
        }
    }
}
