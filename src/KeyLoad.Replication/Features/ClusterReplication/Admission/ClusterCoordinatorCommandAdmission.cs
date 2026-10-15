using System.Text;
using KeyLoad.Core;

namespace KeyLoad.Replication;

/// <summary>Validates one current canonical command before borrowing the node inbox reservation.</summary>
internal static class ClusterCoordinatorCommandAdmission
{
    internal static AdmittedCommand Enqueue(DatabaseEngine database, TimeProvider clock, AdmittedCommandInbox commands,
        Task? worker, ReplicatedOperation operation, CancellationToken cancellationToken)
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
        var principal = database.Store.Read(view => database.Principal(view, evaluated.PrincipalId, evaluated.EvaluatedAt));
        return commands.Enqueue(evaluated, principal, bytes, cancellationToken);
    }

}
