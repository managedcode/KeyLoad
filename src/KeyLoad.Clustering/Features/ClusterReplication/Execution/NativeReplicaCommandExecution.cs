using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Replication;

namespace KeyLoad.Clustering;

/// <summary>Joins an actual node-local command frame around the original receiving queue execution.</summary>
internal static class NativeReplicaCommandExecution
{
    internal static Task StartAsync(ClusterCoordinator coordinator, NativeRequestWorkOwner owner, DatabaseEngine database,
        CancellationToken token)
        => StartAsync(coordinator, owner, database, null, token);

    internal static Task StartAsync(ClusterCoordinator coordinator, NativeRequestWorkOwner owner, DatabaseEngine database,
        IReplicaForwardedCommandObservation? observation, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(database);
        database.RequireResourceLifetimeWorkFenceAttached();
        coordinator.AttachForwardedCommandWork((actualDatabase, clock, cancellationToken)
            => ReferenceEquals(actualDatabase, database)
                ? CreateForwardedAsync(owner, actualDatabase, clock, observation, cancellationToken)
                : Task.FromException<IReplicaForwardedCommandWork>(
                    Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest)));
        coordinator.AttachNativeCommandExecution((consensus, operation, sourcePlanningBudget, receiverWork, cancellationToken)
            => receiverWork is null
                ? ExecuteAsync(owner, database, consensus, operation, sourcePlanningBudget, cancellationToken)
                : ExecuteForwardedAsync(consensus, operation, sourcePlanningBudget, receiverWork, observation, cancellationToken));
        coordinator.RequireNativeCommandExecution();
        return coordinator.StartAsync(token);
    }

    private static async Task<IReplicaForwardedCommandWork> CreateForwardedAsync(NativeRequestWorkOwner owner,
        DatabaseEngine database, TimeProvider clock, IReplicaForwardedCommandObservation? observation,
        CancellationToken cancellationToken)
    {
        var work = await NativeReplicaForwardedCommandWork.CreateAsync(owner, database, clock, cancellationToken).ConfigureAwait(false);
        try
        {
            observation?.Created(work);
            return work;
        }
        catch (Exception error)
        {
            NativeRequestWorkSettlement.Rethrow(await work.CompleteAsync(error).ConfigureAwait(false));
            throw;
        }
    }

    private static async Task<OperationResult> ExecuteForwardedAsync(ReplicaConsensus consensus,
        ReplicatedOperation operation, ReadExecutionBudget? sourcePlanningBudget,
        IReplicaForwardedCommandWork receiverWork, IReplicaForwardedCommandObservation? observation,
        CancellationToken cancellationToken)
    {
        if (sourcePlanningBudget is not null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        receiverWork.Budget.RequireResourceLifetimeBinding(receiverWork.Binding);
        if (observation is not null)
        {
            await observation.BeforeExecuteAsync(operation, receiverWork, cancellationToken).ConfigureAwait(false);
        }
        return await consensus.SubmitAsync(operation, cancellationToken,
            receiverWork.Binding, receiverWork.Budget).ConfigureAwait(false);
    }

    private static async Task<OperationResult> ExecuteAsync(NativeRequestWorkOwner owner, DatabaseEngine database,
        ReplicaConsensus consensus, ReplicatedOperation operation, ReadExecutionBudget? sourcePlanningBudget, CancellationToken token)
    {
        var lease = owner.Acquire(Guid.NewGuid(), NativeRequestWorkKind.CommandCapability);
        CancellationTokenSource? linked = null;
        ResourceLifetimeWorkResultRetention? retention = null;
        OperationResult? result = null;
        Exception? primary = null;
        Exception? cleanup = null;
        try
        {
            retention = new(database.OperationLimitsOptions);
            lease.AttachResultRetention(retention);
            linked = CancellationTokenSource.CreateLinkedTokenSource(token, owner.ShutdownToken, lease.Cancellation);
            result = await consensus.SubmitAsync(operation, linked.Token, lease, sourcePlanningBudget).ConfigureAwait(false);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            primary = error;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            primary = error;
        }
        cleanup = await NativeRequestWorkSettlement.JoinAsync(lease.PrepareCompletionAsync()).ConfigureAwait(false);
        try
        {
            linked?.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            cleanup = NativeRequestWorkSettlement.Preserve(cleanup, error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            cleanup = NativeRequestWorkSettlement.Preserve(cleanup, error);
        }
        try
        {
            retention?.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            cleanup = NativeRequestWorkSettlement.Preserve(cleanup, error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            cleanup = NativeRequestWorkSettlement.Preserve(cleanup, error);
        }
        var failure = await SettleAsync(lease, primary, cleanup).ConfigureAwait(false);
        NativeRequestWorkSettlement.Rethrow(failure);
        return result!;
    }

    private static async Task<Exception?> SettleAsync(NativeRequestWorkLease lease, Exception? primary, Exception? cleanup)
    {
        Exception? failure;
        try
        {
            var settled = await lease.CompleteAsync(primary, cleanup).ConfigureAwait(false);
            failure = NativeRequestWorkSettlement.Preserve(settled.OriginalFailure, settled.CleanupOrJoinFailure);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failure = NativeRequestWorkSettlement.Preserve(NativeRequestWorkSettlement.Preserve(primary, cleanup), error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failure = NativeRequestWorkSettlement.Preserve(NativeRequestWorkSettlement.Preserve(primary, cleanup), error);
        }
        return failure;
    }
}
