using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void ReadQueueDeadlineRetryInputs(IKeyValueView view, ReplicatedOperation operation,
        List<QueueRetryInput> inputs)
    {
        var batch = Payload<CommandRequest>(operation);
        foreach (var request in batch.Mutations.OfType<AdvanceQueueDeadline>())
        {
            if (ReadQueueDeadlineRetryInput(view, batch.Partition, operation.EvaluatedAt, request) is { } input)
            {
                inputs.Add(input);
            }
        }
    }

    private QueueRetryInput? ReadQueueDeadlineRetryInput(IKeyValueView view,
        PartitionRef partition, DateTimeOffset evaluatedAt, AdvanceQueueDeadline request)
    {
        if (request.Kind != QueueDeadlineKind.ExpireLease)
        {
            return null;
        }
        var lane = new QueueLaneRef(partition, request.Queue);
        var resource = Resource(view, partition, request.Queue, ResourceKind.WorkQueue);
        if (resource.Paused || DispatchPaused(view) || resource.QueuePolicy.RetryJitter != QueueRetryJitter.Full)
        {
            return null;
        }
        var metadata = RequireQueueDeadlineState(view, lane, request, evaluatedAt);
        if (metadata.ExpiresAt <= evaluatedAt || metadata.Attempts >= resource.QueuePolicy.MaxAttempts)
        {
            return null;
        }
        var key = QueueKey(LeasedQueueSpace, lane, request.ExpectedDeadline, metadata.Id);
        if (view.GetRecord<string>(key) != metadata.Id)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference);
        }
        RequireCurrentQueueDueEntry(lane, metadata, evaluatedAt, leased: true, key);
        return new(lane, resource.QueuePolicy, metadata, key);
    }
}
