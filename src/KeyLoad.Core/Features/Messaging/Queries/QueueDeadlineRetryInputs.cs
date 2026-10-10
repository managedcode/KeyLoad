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
            if (request.Kind != QueueDeadlineKind.ExpireLease)
            { continue; }
            var lane = new QueueLaneRef(batch.Partition, request.Queue);
            var resource = Resource(view, batch.Partition, request.Queue, ResourceKind.WorkQueue);
            if (resource.Paused || DispatchPaused(view) || resource.QueuePolicy.RetryJitter != QueueRetryJitter.Full)
            { continue; }
            var metadata = RequireQueueDeadlineState(view, lane, request, operation.EvaluatedAt);
            if (metadata.ExpiresAt <= operation.EvaluatedAt || metadata.Attempts >= resource.QueuePolicy.MaxAttempts)
            { continue; }
            var key = QueueKey(LeasedQueueSpace, lane, request.ExpectedDeadline, metadata.Id);
            if (view.GetRecord<string>(key) != metadata.Id)
            { throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference); }
            RequireCurrentQueueDueEntry(lane, metadata, operation.EvaluatedAt, leased: true, key);
            inputs.Add(new(lane, resource.QueuePolicy, metadata, key));
        }
    }
}
