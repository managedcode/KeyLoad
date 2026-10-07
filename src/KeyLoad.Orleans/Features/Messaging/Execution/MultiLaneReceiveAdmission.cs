using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal static class MultiLaneReceiveAdmission
{
    private const int NoRequestedMessages = 0;
    private const int NoRequestedPayloadBytes = 0;
    private const int NoLeaseDurationSeconds = 0;
    internal const string Invalid = "The multi-lane receive request is invalid.";
    internal const string Interrupted = MultiLaneReceiveProtocol.Interrupted;
    internal const string Exceeded = "The multi-lane receive budget was exceeded.";

    internal static void Validate(MultiLaneReceiveRequest request, Guid commandId,
        MessagingExecutionOptions messaging, DatabaseLimits limits, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.RequestId == Guid.Empty || request.RequestId != commandId || request.Requests.IsDefaultOrEmpty)
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        if (request.Requests.Length > messaging.MaximumReceiveLanes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, Exceeded); }
        var ids = new HashSet<Guid> { request.RequestId };
        var lanes = new HashSet<QueueLaneRef>();
        long messages = NoRequestedMessages;
        long bytes = NoRequestedPayloadBytes;
        foreach (var leaf in request.Requests)
        {
            token.ThrowIfCancellationRequested();
            if (leaf is null || leaf.Lane is null || leaf.Lane.Partition is null
                || leaf.RequestId == Guid.Empty || !ids.Add(leaf.RequestId) || !lanes.Add(leaf.Lane)
                || leaf.MaxMessages <= NoRequestedMessages || leaf.MaxBytes <= NoRequestedPayloadBytes
                || leaf.LeaseSeconds <= NoLeaseDurationSeconds)
            { throw Errors.Fail(ErrorCode.Validation, Invalid); }
            DatabaseEngine.ValidatePartition(leaf.Lane.Partition);
            JsonData.Identifier(leaf.Lane.Queue);
            messages += leaf.MaxMessages;
            bytes += leaf.MaxBytes;
            if (messages > messaging.MaximumReceiveMessages || bytes > limits.MaxBatchBytes)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, Exceeded); }
        }
    }
}
