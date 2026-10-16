using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Replication;

/// <summary>Bounded node admission and stable-ID outcomes over the Orleans replica protocol.</summary>
public sealed class ClusterCoordinator : ICommitCoordinator, IHostedService, IAsyncDisposable
{
    private readonly ReplicaConsensus consensus;
    private readonly DatabaseEngine database;
    private readonly TimeProvider clock;
    private readonly TimeSpan commandTimeout;
    private readonly AdmittedCommandInbox commands;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Lock lifecycle = new();
    private Func<ReplicaConsensus, ReplicatedOperation, ReadExecutionBudget?, IReplicaForwardedCommandWork?, CancellationToken, Task<OperationResult>>? nativeCommandExecution;
    private Task? worker;
    private Task? shutdown;
    private Task? disposal;

    /// <summary>Creates admission without starting transport or waiting for membership.</summary>
    /// <param name="consensus">Physical node protocol whose wire transport is supplied by Orleans.</param>
    /// <param name="database">Node-owned canonical engine and persisted admission principals.</param>
    /// <param name="admission">Configured independent control and data admission budgets.</param>
    /// <param name="clock">Node system clock; a caller cannot choose leader evaluation time.</param>
    /// <param name="options">Centrally validated command deadline, frozen for this admission owner.</param>
    /// <param name="inboxOptions">Centrally validated command lane scheduling policy.</param>
    public ClusterCoordinator(ReplicaConsensus consensus, DatabaseEngine database,
        CommandAdmissionGovernor admission, TimeProvider clock, IOptions<ReplicaExecutionOptions> options,
        IOptions<CommandInboxExecutionOptions> inboxOptions)
    {
        ArgumentNullException.ThrowIfNull(consensus);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Value;
        settings.Validate();
        commandTimeout = settings.CommandTimeout;
        this.consensus = consensus;
        this.database = database;
        this.clock = clock;
        commands = new(admission, inboxOptions);
        database.Durability = DurabilityProfile.QuorumProcessDurable;
        consensus.ConfigureForwardedCommandAdmission(AcceptForwardedAsync);
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
        ReadOnlyMemory<byte> payload, ReadExecutionBudget? sourcePlanningBudget = null,
        CancellationToken cancellationToken = default)
        => AdmitAsync(database.CreateNativeOperation(kind, id, principalId, clock.GetUtcNow(), payload),
            cancellationToken, sourcePlanningBudget);

    /// <inheritdoc />
    public Task<OperationResult> SubmitVerifiedAsync(ReplicatedOperation operation, CancellationToken cancellationToken,
        ReadExecutionBudget? sourcePlanningBudget = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return AdmitAsync(database.VerifyOperationAuthority(operation), cancellationToken, sourcePlanningBudget);
    }

    internal Task<OperationResult> AdmitOnlineTextPublication(ReplicatedOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Kind != OperationKind.OnlineTextPublicationPhase)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ReplicaProtocol.InvalidAppend);
        }
        return Enqueue(database.VerifyOperationAuthority(operation), cancellationToken).Completion;
    }

    internal Task<OperationResult> AdmitOnlineProjectionCheckpoint(ReplicatedOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Kind != OperationKind.CommitProjectionBatch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ReplicaProtocol.InvalidAppend);
        }
        return Enqueue(database.VerifyOperationAuthority(operation), cancellationToken).Completion;
    }

    private async Task<OperationResult> AdmitAsync(ReplicatedOperation operation, CancellationToken cancellationToken,
        ReadExecutionBudget? sourcePlanningBudget = null)
    {
        var pending = Enqueue(operation, cancellationToken, sourcePlanningBudget);
        try
        {
            return await pending.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, ReplicaProtocol.InterruptedWrite);
        }
    }

    private AdmittedCommand Enqueue(ReplicatedOperation operation, CancellationToken cancellationToken,
        ReadExecutionBudget? sourcePlanningBudget = null, IReplicaForwardedCommandWork? receiverWork = null)
        => ClusterCoordinatorCommandAdmission.Enqueue(database, clock, commands, worker, operation,
            cancellationToken, sourcePlanningBudget, receiverWork);

    /// <summary>Admits an authenticated forwarded operation only on a ready current leader.</summary>
    /// <param name="operation">Stable-ID operation forwarded by an authenticated replica.</param>
    /// <param name="cancellationToken">Cancellation while checking leadership and awaiting admission.</param>
    /// <returns>The persisted operation outcome after bounded leader admission.</returns>
    public Task<OperationResult> AcceptForwardedAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
        => AcceptForwardedAsync(operation, null, cancellationToken);

    private async Task<OperationResult> AcceptForwardedAsync(ReplicatedOperation operation,
        IReplicaForwardedCommandWork? receiverWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!await consensus.IsLeaderAsync(cancellationToken).ConfigureAwait(false))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader);
        }
        if (receiverWork is null)
        {
            consensus.RequireLowerForwardedAdmission();
            return await AdmitAsync(database.NormalizeOperation(operation), cancellationToken).ConfigureAwait(false);
        }
        receiverWork.Budget.Check();
        var pending = Enqueue(database.NormalizeOperation(operation), cancellationToken, null, receiverWork);
        return await ReplicaForwardedCommandCompletion.AwaitAsync(pending, receiverWork, cancellationToken).ConfigureAwait(false);
    }

    internal void AttachForwardedCommandWork(
        Func<DatabaseEngine, TimeProvider, CancellationToken, Task<IReplicaForwardedCommandWork>> factory)
        => consensus.AttachForwardedCommandWork(factory);

    /// <inheritdoc />
    public async Task ReadBarrierAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await consensus.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException original) when (!cancellationToken.IsCancellationRequested)
        {
            throw ReplicaReadFailureMetadata.Preserve(original,
                Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader));
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

    internal void AttachNativeCommandExecution(
        Func<ReplicaConsensus, ReplicatedOperation, ReadExecutionBudget?, IReplicaForwardedCommandWork?, CancellationToken, Task<OperationResult>> execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        lock (lifecycle)
        {
            if (nativeCommandExecution is not null || worker is not null || shutdown is not null)
            {
                throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.InvalidPeer);
            }
            nativeCommandExecution = execution;
        }
    }

    internal void RequireNativeCommandExecution()
    {
        if (nativeCommandExecution is null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.InvalidPeer);
        }
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await commands.ReadAsync(stoppingToken).ConfigureAwait(false) is { } pending)
            {
                await ClusterCoordinatorCommandExecution.ExecuteAsync(consensus, pending, clock, commandTimeout,
                    nativeCommandExecution, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { commands.Stop(); }
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

    private Task StopCoreAsync()
        => ReplicaCoordinatorShutdown.DrainAsync(commands, lifetime, worker ?? Task.CompletedTask);

    private async Task DisposeCoreAsync()
    {
        var failures = await ReplicaCoordinatorShutdown.JoinDisposalAsync(StopAsync(CancellationToken.None)).ConfigureAwait(false);
        try
        {
            await ReplicaShutdownStage.ObserveAsync(commands.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
            DisposeLifetime(failures);
            throw;
        }
        DisposeLifetime(failures);
    }

    private void DisposeLifetime(List<Exception> failures)
    {
        try
        {
            lifetime.Dispose();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
            ReplicaCoordinatorShutdown.ThrowIfAny(failures);
            throw;
        }
        ReplicaCoordinatorShutdown.ThrowIfAny(failures);
    }

    /// <summary>Drains the queue once; DI aliases do not dispose its lifetime source twice.</summary>
    /// <returns>Completion after shared shutdown and admission resource disposal.</returns>
    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            disposal ??= DisposeCoreAsync();
            return new(disposal);
        }
    }
}
