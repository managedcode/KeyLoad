using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class MultiLaneReceiveAdmission
{
    internal const string Invalid = "The multi-lane receive request is invalid.";
    internal const string Interrupted = MultiLaneReceiveProtocol.Interrupted;
    internal const string Exceeded = "The multi-lane receive budget was exceeded.";

    internal static void Validate(MultiLaneReceiveRequest request, Guid commandId,
        MessagingExecutionOptions messaging, DatabaseLimits limits, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.RequestId == Guid.Empty || request.RequestId != commandId || request.Requests.IsDefaultOrEmpty)
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
        if (request.Requests.Length > messaging.MaximumReceiveLanes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Exceeded);
        }
        var ids = new HashSet<Guid> { request.RequestId };
        var lanes = new HashSet<QueueLaneRef>();
        long messages = 0;
        long bytes = 0;
        foreach (var leaf in request.Requests)
        {
            token.ThrowIfCancellationRequested();
            if (leaf is null || leaf.Lane is null || leaf.Lane.Partition is null
                || leaf.RequestId == Guid.Empty || !ids.Add(leaf.RequestId) || !lanes.Add(leaf.Lane)
                || leaf.MaxMessages <= 0 || leaf.MaxBytes <= 0
                || leaf.LeaseSeconds <= 0)
            {
                throw Errors.Fail(ErrorCode.Validation, Invalid);
            }
            EngineResourceAccess.ValidatePartition(leaf.Lane.Partition);
            JsonData.Identifier(leaf.Lane.Queue);
            messages += leaf.MaxMessages;
            bytes += leaf.MaxBytes;
            if (messages > messaging.MaximumReceiveMessages || bytes > limits.MaxBatchBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, Exceeded);
            }
        }
    }
}
