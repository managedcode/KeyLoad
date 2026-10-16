using System.Text;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Replication;

/// <summary>Validates one current canonical command before borrowing the node inbox reservation.</summary>
internal static class ClusterCoordinatorCommandAdmission
{
    internal static AdmittedCommand Enqueue(DatabaseEngine database, TimeProvider clock, AdmittedCommandInbox commands,
        Task? worker, ReplicatedOperation operation, CancellationToken cancellationToken,
        ReadExecutionBudget? sourcePlanningBudget = null, IReplicaForwardedCommandWork? receiverWork = null)
    {
        ArgumentNullException.ThrowIfNull(operation.PrincipalId);
        cancellationToken.ThrowIfCancellationRequested();
        if (worker is null || worker.IsCompleted)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader);
        }
        var bytes = Math.Max(Encoding.UTF8.GetByteCount(operation.PayloadJson), operation.NativePayload.Length);
        if (operation.Id == Guid.Empty || bytes > database.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaProtocol.InvalidAppend);
        }
        var evaluated = operation with
        {
            EvaluatedAt = clock.GetUtcNow()
        };
        var work = receiverWork?.Budget ?? sourcePlanningBudget;
        work?.Check();
        var principal = database.Store.Read(raw => database.Principal(
            work is null ? raw : work.CreateView(raw), evaluated.PrincipalId, evaluated.EvaluatedAt));
        work?.Check();
        return receiverWork is null
            ? commands.Enqueue(evaluated, principal, bytes, sourcePlanningBudget, cancellationToken)
            : commands.EnqueueForwarded(evaluated, principal, bytes, receiverWork, cancellationToken);
    }

}
