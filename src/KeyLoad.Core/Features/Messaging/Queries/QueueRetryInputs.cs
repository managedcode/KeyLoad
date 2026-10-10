using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private List<QueueRetryInput> ReadQueueRetryInputs(IKeyValueView view, PrincipalRecord principal, ReplicatedOperation operation)
    {
        var inputs = new List<QueueRetryInput>();
        if (operation.Kind == OperationKind.Delivery)
        {
            var request = Payload<DeliveryCommand>(operation);
            var policy = Resource(view, request.Lane.Partition, request.Lane.Queue, ResourceKind.WorkQueue).QueuePolicy;
            if (request.Action != DeliveryAction.Nack || policy.RetryJitter != QueueRetryJitter.Full)
            { return inputs; }
            var lease = Lease(view, principal, request.Lane, request.Token, operation.EvaluatedAt);
            if (lease.Metadata.Attempts < policy.MaxAttempts)
            { inputs.Add(new(request.Lane, policy, lease.Metadata, ReadOnlyMemory<byte>.Empty)); }
        }
        if (operation.Kind == OperationKind.Receive)
        {
            var request = Payload<ReceiveRequest>(operation);
            var resource = Resource(view, request.Lane.Partition, request.Lane.Queue, ResourceKind.WorkQueue);
            if (resource.QueuePolicy.RetryJitter != QueueRetryJitter.Full || resource.Paused || DispatchPaused(view))
            { return inputs; }
            RequireQueueRetryReceive(request, resource.QueuePolicy);
            ReadQueueLeaseRetryInputs(view, request.Lane, resource.QueuePolicy, operation.EvaluatedAt, inputs);
        }
        if (operation.Kind == OperationKind.Batch)
        { ReadQueueDeadlineRetryInputs(view, operation, inputs); }
        return inputs;
    }

    private void RequireQueueRetryReceive(ReceiveRequest request, QueuePolicy policy)
    {
        if (request.MaxMessages < MessagingMinimumPositiveCount || request.MaxMessages > messagingExecution.MaximumReceiveMessages
            || request.MaxBytes < MessagingMinimumPositiveCount || request.MaxBytes > Limits.MaxBatchBytes
            || request.LeaseSeconds < MessagingMinimumPositiveCount || request.LeaseSeconds > policy.MaxLeaseSeconds)
        { throw Errors.Fail(ErrorCode.Validation, InvalidReceiveBudget); }
    }

    private void ReadQueueLeaseRetryInputs(IKeyValueView view, QueueLaneRef lane, QueuePolicy policy,
        DateTimeOffset now, List<QueueRetryInput> inputs)
    {
        var page = view.Scan(QueueKey(LeasedQueueSpace, lane), messagingExecution.QueueScanPageSize);
        foreach (var row in page.Records)
        {
            var key = KeyCodec.Decode(row.Key.Span);
            if ((DateTimeOffset)key[^QueueSweepTransitionsNamespaceComponentFromEnd]! > now)
            { break; }
            var id = NativeSerialization.Deserialize<string>(row.Value.Span);
            var metadata = view.GetRecord<MessageMetadata>(QueueKey(MessageMetadataSpace, lane, id))
                ?? throw Errors.Fail(ErrorCode.Corruption, MissingIndexedMetadata);
            RequireCurrentQueueDueEntry(lane, metadata, now, leased: true, row.Key.Span);
            if (metadata.ExpiresAt <= now || metadata.Attempts >= policy.MaxAttempts)
            { continue; }
            inputs.Add(new(lane, policy, metadata, row.Key.ToArray()));
        }
    }
}
