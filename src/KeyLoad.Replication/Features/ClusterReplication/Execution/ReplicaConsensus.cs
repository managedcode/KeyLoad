using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Replication;

/// <summary>Fixed-voter node protocol hosted by Orleans; activation lifetime never owns canonical storage.</summary>
public sealed class ReplicaConsensus : IReplicaEndpoint, IAsyncDisposable
{
    private const int NoLeaderReadyPosition = 0;

    private readonly ReplicaState state;
    private readonly ReplicaRpcClient rpc;
    private readonly ReplicaElection election;
    private readonly ReplicaLeader leader;
    private readonly ReplicaFollowerSender followers;
    private readonly ReplicaAppendReceiver appends;
    private readonly ReplicaSnapshotReceiver snapshots;
    private readonly ReplicaMaintenance maintenance;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource transportReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ReplicaActivityTracker activity = new();
    private readonly Lock lifecycle = new();
    private readonly CancellationToken stoppingToken;
    private readonly ReplicaRequestDispatcher dispatcher;
    private readonly ReplicaReadRoundExecutor reads;
    private readonly ReplicaExecutionOptions execution;
    private Task? worker;
    private Task? shutdown;
    private Task? disposal;

    /// <summary>Recovers node-owned state without resolving Orleans clients or waiting for quorum.</summary>
    /// <param name="materializer">Node-owned ordered canonical apply and checkpoint worker.</param>
    /// <param name="configurationOptions">Centrally validated fixed voter scope, quorum and bounded protocol settings.</param>
    /// <param name="options">Centrally validated command and read deadlines, frozen for this protocol owner.</param>
    /// <param name="clock">Injected runtime clock, defaulting to the system clock.</param>
    /// <param name="logger">Optional logger for fenced maintenance failures.</param>
    public ReplicaConsensus(ReplicaMaterializer materializer, IOptions<ReplicaConfiguration> configurationOptions,
        IOptions<ReplicaExecutionOptions> options, TimeProvider? clock = null,
        ILogger<ReplicaConsensus>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(materializer);
        ArgumentNullException.ThrowIfNull(configurationOptions);
        var configuration = configurationOptions.Value;
        ArgumentNullException.ThrowIfNull(options);
        execution = options.Value;
        execution.Validate();
        configuration.Validate();
        stoppingToken = lifetime.Token;
        state = new(materializer, configurationOptions, clock ?? TimeProvider.System);
        rpc = new(configurationOptions, options, state.Clock, stoppingToken);
        election = new(state, rpc);
        followers = new(state, rpc, execution.MaximumSnapshotChunksPerRound);
        leader = new(state, followers);
        appends = new(state);
        snapshots = new(state);
        maintenance = new(state, election, leader, logger);
        dispatcher = new(election, appends, leader, snapshots, configurationOptions);
        reads = new(state, rpc, leader, activity, transportReady.Task, execution.ReadBarrierTimeout, stoppingToken);
    }

    /// <inheritdoc />
    public Task TransportReady => transportReady.Task;

    /// <inheritdoc />
    public void AttachTransport(IReplicaTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        lock (lifecycle)
        {
            if (shutdown is not null)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader);
            }
            rpc.Attach(transport);
            state.Ready = true;
            worker = Task.Run(() => maintenance.RunAsync(stoppingToken));
            transportReady.TrySetResult();
        }
    }

    /// <summary>Connects forwarded writes to the leader's bounded admission queue.</summary>
    /// <param name="accept">One-time admission callback for authenticated forwarded operations.</param>
    public void ConfigureForwarding(Func<ReplicatedOperation, CancellationToken, Task<OperationResult>> accept)
    {
        ArgumentNullException.ThrowIfNull(accept);
        dispatcher.ConfigureForwarding(accept);
    }

    /// <summary>Returns diagnostic node state without claiming quorum readiness.</summary>
    /// <param name="cancellationToken">Cancellation while waiting for the protocol state gate.</param>
    /// <returns>A consistent snapshot of local protocol and canonical apply state.</returns>
    public async Task<ReplicaNodeState> StateAsync(CancellationToken cancellationToken)
    {
        using var active = activity.Enter();
        return await state.LockedAsync(state.Snapshot, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Observes native checkpoint prefix and owned task state for the armed diagnostic readiness profile.</summary>
    /// <param name="cancellationToken">Cancellation while waiting for the original protocol state gate.</param>
    /// <returns>Local checkpoint metadata sampled with maintenance task registration; it does not fence future work.</returns>
    public async Task<ReplicaNodeState> ReadCheckpointStateAsync(CancellationToken cancellationToken)
    {
        using var active = activity.Enter();
        return await state.LockedAsync(maintenance.ReadState, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Checks whether this physical replica owns current, committed-term leadership.</summary>
    /// <param name="cancellationToken">Cancellation while waiting for the protocol state gate.</param>
    /// <returns>True when this leader's current-term readiness entry is committed.</returns>
    public async Task<bool> IsLeaderAsync(CancellationToken cancellationToken)
    {
        using var active = activity.Enter();
        return await state.LockedAsync(() => state.Role == ReplicaRole.Leader && state.LeaderReadyIndex > NoLeaderReadyPosition
            && state.Log.State.CommittedIndex >= state.LeaderReadyIndex, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Commits locally or forwards through the authenticated Orleans replica service.</summary>
    /// <param name="operation">Stable-ID operation to commit or forward to the current leader.</param>
    /// <param name="cancellationToken">Caller cancellation within the bounded command deadline.</param>
    /// <returns>The persisted outcome after quorum commit and ordered canonical apply.</returns>
    public async Task<OperationResult> SubmitAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var active = activity.Enter();
        operation = ReplicaOperationAuthority.Verify(operation, state.Materializer.Database)!;
        using var deadline = new CancellationTokenSource(execution.CommandTimeout, state.Clock);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token, deadline.Token);
        await TransportReady.WaitAsync(request.Token).ConfigureAwait(false);
        var route = await state.LockedAsync(() => (state.Role, state.LeaderId), request.Token).ConfigureAwait(false);
        if (route.Role == ReplicaRole.Leader)
        {
            return await leader.SubmitAsync(operation, request.Token).ConfigureAwait(false);
        }
        if (route.LeaderId is null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader);
        }
        return await rpc.InvokeAsync<ReplicatedOperation, OperationResult>(route.LeaderId, ReplicaRpc.Forward, operation, request.Token).ConfigureAwait(false);
    }

    /// <summary>Establishes a current-term quorum cut and waits for this node's canonical apply.</summary>
    /// <param name="cancellationToken">Caller cancellation within the bounded read barrier deadline.</param>
    /// <returns>Completion after the authenticated quorum cut is applied locally.</returns>
    public Task ReadBarrierAsync(CancellationToken cancellationToken)
        => reads.ExecuteAsync(ReplicaReadRoundPurpose.Application, cancellationToken);

    /// <summary>Returns the node's current canonical position for change-wait registration.</summary>
    public long AppliedPosition => state.Materializer.AppliedPosition;

    /// <summary>Waits for a canonical apply change without retaining a store view or handle.</summary>
    /// <param name="observedPosition">The last applied position observed by the caller.</param>
    /// <param name="cancellationToken">Cancellation of this caller's wait.</param>
    /// <returns>The changed canonical position.</returns>
    public async Task<long> WaitForAppliedPositionChangeAsync(long observedPosition, CancellationToken cancellationToken)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stoppingToken);
        return await state.Materializer.WaitForAppliedPositionChangeAsync(observedPosition, request.Token)
            .ConfigureAwait(false);
    }

    /// <summary>Acquires a quorum-backed cut for trusted native membership without spending application read admission.</summary>
    /// <param name="cancellationToken">Native membership caller cancellation within the unchanged read deadline.</param>
    /// <returns>Completion after the current-term majority cut is applied locally.</returns>
    public Task ReadControlBarrierAsync(CancellationToken cancellationToken)
        => reads.ExecuteControlAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<byte>> HandleAsync(ReplicaRpc method, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        using var active = activity.Enter();
        using var deadline = new CancellationTokenSource(ReplicaRpcDeadline.SelectIncoming(method, execution), state.Clock);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token, deadline.Token);
        request.Token.ThrowIfCancellationRequested();
        if (!state.Ready)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader);
        }
        return await dispatcher.HandleAsync(method, payload, request.Token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            shutdown ??= StopCoreAsync();
            return shutdown.WaitAsync(cancellationToken);
        }
    }

    private Task StopCoreAsync()
        => ReplicaShutdownStage.RunAsync(activity, state, lifetime, transportReady, worker,
            followers, snapshots, maintenance, stoppingToken);

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        await ReplicaShutdownStage.ObserveAsync(StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        try
        {
            leader.Dispose();
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            failures.Add(failure);
        }
        try
        {
            snapshots.Dispose();
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            failures.Add(failure);
        }
        try
        {
            followers.Dispose();
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            failures.Add(failure);
        }
        try
        {
            lifetime.Dispose();
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            failures.Add(failure);
        }
        ReplicaShutdownStage.ThrowIfAny(failures);
    }

    /// <summary>Stops protocol work before the composition root disposes node-owned storage.</summary>
    /// <returns>Completion after the shared shutdown task drains every protocol owner.</returns>
    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            disposal ??= DisposeCoreAsync();
            return new(disposal);
        }
    }
}
