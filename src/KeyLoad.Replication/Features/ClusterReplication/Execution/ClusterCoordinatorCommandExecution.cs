using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Replication;

/// <summary>Settles one original queue execution under its existing command deadline.</summary>
internal static class ClusterCoordinatorCommandExecution
{
    internal static async Task ExecuteAsync(ReplicaConsensus consensus, AdmittedCommand pending,
        TimeProvider clock, TimeSpan commandTimeout,
        Func<ReplicaConsensus, ReplicatedOperation, ReadExecutionBudget?, IReplicaForwardedCommandWork?, CancellationToken, Task<OperationResult>>? nativeCommandExecution,
        CancellationToken stoppingToken)
    {
        using var timeout = new CancellationTokenSource(commandTimeout, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken, timeout.Token, pending.ReceiverWork?.Cancellation ?? CancellationToken.None);
        try
        {
            var execution = nativeCommandExecution;
            var result = execution is null
                ? await ExecuteLowerAsync(consensus, pending, deadline.Token).ConfigureAwait(false)
                : await execution(consensus, pending.Operation, pending.SourcePlanningBudget, pending.ReceiverWork, deadline.Token).ConfigureAwait(false);
            pending.Complete(result);
        }
        catch (KeyLoadException error) { pending.Fail(error); }
        catch (OperationCanceledException)
        {
            pending.Fail(Errors.Fail(ErrorCode.UnknownWriteOutcome, ReplicaProtocol.InterruptedWrite));
        }
        catch (Exception error)
        {
            pending.Fail(CqrsRuntimeFailures.FindFatal(error)
                ?? Errors.Fail(ErrorCode.UnknownWriteOutcome, ReplicaProtocol.InterruptedWrite));
            throw;
        }
    }

    private static Task<OperationResult> ExecuteLowerAsync(ReplicaConsensus consensus,
        AdmittedCommand pending, CancellationToken cancellationToken)
    {
        if (pending.ReceiverWork is not null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.InvalidPeer);
        }
        return consensus.SubmitAsync(pending.Operation, cancellationToken, null, pending.SourcePlanningBudget);
    }

}
