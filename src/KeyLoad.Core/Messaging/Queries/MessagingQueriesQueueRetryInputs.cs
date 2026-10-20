using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes MessagingQueriesQueueRetryInputs operations against the caller's database context.</summary>
public static class MessagingQueriesQueueRetryInputs
{
    internal static List<QueueRetryInput> ReadQueueRetryInputs(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        var inputs = new List<QueueRetryInput>();
        if (operation.Value!.Kind == OperationKind.Delivery)
        {
            var request = OperationProtocol.Payload<DeliveryCommand>(operation);
            var policy = engine.Resource(view, request.Lane.Partition, request.Lane.Queue, ResourceKind.WorkQueue).QueuePolicy;
            if (request.Action != DeliveryAction.Nack || policy.RetryJitter != QueueRetryJitter.Full)
            {
                return inputs;
            }

            var lease = engine.Lease(view, principal, request.Lane, request.Token, operation.Timestamp);
            if (lease.Metadata.Attempts < policy.MaxAttempts)
            {
                inputs.Add(new(request.Lane, policy, lease.Metadata, ReadOnlyMemory<byte>.Empty));
            }
        }

        if (operation.Value!.Kind == OperationKind.Receive)
        {
            var request = OperationProtocol.Payload<ReceiveRequest>(operation);
            var resource = engine.Resource(view, request.Lane.Partition, request.Lane.Queue, ResourceKind.WorkQueue);
            if (resource.QueuePolicy.RetryJitter != QueueRetryJitter.Full || resource.Paused || engine.DispatchPaused(view))
            {
                return inputs;
            }

            engine.RequireQueueRetryReceive(request, resource.QueuePolicy);
            engine.ReadQueueLeaseRetryInputs(view, request.Lane, resource.QueuePolicy, operation.Timestamp, inputs);
        }

        if (operation.Value!.Kind == OperationKind.Batch)
        {
            engine.ReadQueueDeadlineRetryInputs(view, operation, inputs);
        }

        return inputs;
    }

    internal static void RequireQueueRetryReceive(this DatabaseEngine engine, ReceiveRequest request, QueuePolicy policy)
    {
        if (request.MaxMessages < MessagingExecutionMessaging.MessagingMinimumPositiveCount || request.MaxMessages > engine.MessagingExecution.MaximumReceiveMessages || request.MaxBytes < MessagingExecutionMessaging.MessagingMinimumPositiveCount || request.MaxBytes > engine.Limits.MaxBatchBytes || request.LeaseSeconds < MessagingExecutionMessaging.MessagingMinimumPositiveCount || request.LeaseSeconds > policy.MaxLeaseSeconds)
        {
            throw Errors.Fail(ErrorCode.Validation, MessagingCommandsQueueSweepTransitions.InvalidReceiveBudget);
        }
    }

    internal static void ReadQueueLeaseRetryInputs(this DatabaseEngine engine, IKeyValueView view, QueueLaneRef lane, QueuePolicy policy, DateTimeOffset now, List<QueueRetryInput> inputs)
    {
        var page = view.Scan(MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.LeasedQueueSpace, lane), engine.MessagingExecution.QueueScanPageSize);
        foreach (var row in page.Records)
        {
            var key = KeyCodec.Decode(row.Key.Span);
            if ((DateTimeOffset)key[^MessagingCommandsQueueSweepTransitions.QueueSweepTransitionsNamespaceComponentFromEnd]! > now)
            {
                break;
            }

            var id = OrleansSerialization.Deserialize<string>(row.Value.Span);
            var metadata = view.GetRecord<MessageMetadata>(MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.MessageMetadataSpace, lane, id)) ?? throw Errors.Fail(ErrorCode.Corruption, MessagingCommandsQueueSweepTransitions.MissingIndexedMetadata);
            MessagingValidationQueueDeadlinesQueueDueAuthority.RequireCurrentQueueDueEntry(lane, metadata, now, leased: true, row.Key.Span);
            if (metadata.ExpiresAt <= now || metadata.Attempts >= policy.MaxAttempts)
            {
                continue;
            }

            inputs.Add(new(lane, policy, metadata, row.Key.ToArray()));
        }
    }
}
