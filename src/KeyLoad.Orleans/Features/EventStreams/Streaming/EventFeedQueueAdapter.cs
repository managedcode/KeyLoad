using global::Orleans.Streams;

namespace KeyLoad.Orleans;

internal sealed class EventFeedQueueAdapter(IQueueAdapter original, EventFeedHintBatchAdmission admission)
    : IQueueAdapter
{
    public string Name => original.Name;
    public bool IsRewindable => original.IsRewindable;
    public StreamProviderDirection Direction => original.Direction;
    public IQueueAdapterReceiver CreateReceiver(QueueId queueId) => original.CreateReceiver(queueId);

    public Task QueueMessageBatchAsync<T>(StreamId streamId, IEnumerable<T> events,
        StreamSequenceToken? token, Dictionary<string, object>? requestContext)
    {
        var hint = admission.Require(events, requestContext);
        return original.QueueMessageBatchAsync(streamId, new[] { hint }, token, null!);
    }
}
