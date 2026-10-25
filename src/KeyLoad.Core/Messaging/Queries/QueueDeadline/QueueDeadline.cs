using KeyLoad.Core.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes QueueDeadline operations against the caller's database context.</summary>
public static class QueueDeadline
{
    internal static void ReadQueueDeadlineRetryInputs(this DatabaseEngine engine, IKeyValueView view, Command<DatabaseCommand> operation, List<QueueRetryInput> inputs)
    {
        var batch = OperationProtocol.Payload<CommandRequest>(operation);
        foreach (var request in batch.Mutations.OfType<AdvanceQueueDeadline>())
        {
            if ((engine.ReadQueueDeadlineRetryInput(view, batch.Partition, operation.Timestamp, request)).Value is { } input)
            {
                inputs.Add(input);
            }
        }
    }

    internal static global::ManagedCode.Communication.Result<QueueRetryInput?> ReadQueueDeadlineRetryInput(this DatabaseEngine engine, IKeyValueView view, PartitionRef partition, DateTimeOffset evaluatedAt, AdvanceQueueDeadline request)
    {
        if (request.Kind != QueueDeadlineKind.ExpireLease)
        {
            return global::ManagedCode.Communication.Result<QueueRetryInput?>.FailNotFound(nameof(ReadQueueDeadlineRetryInput));
        }

        var lane = new QueueLaneRef(partition, request.Queue);
        var resource = engine.Resource(view, partition, request.Queue, ResourceKind.WorkQueue);
        if (resource.Paused || engine.DispatchPaused(view) || resource.QueuePolicy.RetryJitter != QueueRetryJitter.Full)
        {
            return global::ManagedCode.Communication.Result<QueueRetryInput?>.FailNotFound(nameof(ReadQueueDeadlineRetryInput));
        }

        var metadata = MessagingValidationQueueDeadlines.RequireQueueDeadlineState(view, lane, request, evaluatedAt);
        if (metadata.ExpiresAt <= evaluatedAt || metadata.Attempts >= resource.QueuePolicy.MaxAttempts)
        {
            return global::ManagedCode.Communication.Result<QueueRetryInput?>.FailNotFound(nameof(ReadQueueDeadlineRetryInput));
        }

        var key = MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.LeasedQueueSpace, lane, request.ExpectedDeadline, metadata.Id);
        if (view.GetRecord<string>(key) != metadata.Id)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference);
        }

        QueueDueAuthority.RequireCurrentQueueDueEntry(lane, metadata, evaluatedAt, leased: true, key);
        return global::ManagedCode.Communication.Result<QueueRetryInput?>.Succeed(((QueueRetryInput?)(new(lane, resource.QueuePolicy, metadata, key))));
    }
}
