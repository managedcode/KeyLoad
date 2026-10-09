using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

internal sealed partial class PartitionMovementTransferSourceReads
{
    private void CompleteOpen(PrincipalRecord principal, PartitionMovementTransferDataCapability query, Guid requestId,
        PartitionMovementPendingTransferRead producer, ReadExecutionBudget work)
    {
        var (completed, failure) = PartitionMovementSourceReadProducer.Run(workOwner, work, producer,
            originalWork => PartitionMovementTransferSourceCapture.Capture(database, memory, principal.Id, query, originalWork),
            (handle, session, retainedWork) =>
            {
                lock (gate)
                {
                    requireMoveOpen(producer.Partition, producer.MoveId);
                    sessions.Add(handle.HandleId, new PartitionMovementTransferSourceEntry(producer.Partition,
                        producer.MoveId, requestId, principal.Id, query, handle, session, retainedWork));
                }
            }, shutdown);
        var failures = new List<Exception>();
        if (failure is not null)
        { failures.Add(failure); }
        ServerFailureObserver.Observe(producer.Dispose, failures);
        lock (gate)
        {
            if (failures.Count != PartitionMovementProtocol.NoFailures)
            { producer.Completion.TrySetException(failures); }
            else if (completed is not null)
            { producer.Completion.TrySetResult(completed); }
            else
            { producer.Completion.TrySetException(Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority)); }
            captures.Remove(requestId);
        }
    }
}
