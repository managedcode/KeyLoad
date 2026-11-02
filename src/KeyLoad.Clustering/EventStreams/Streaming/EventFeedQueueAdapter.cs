using global::Orleans.Streams;

namespace KeyLoad.Clustering;

internal sealed class EventFeedQueueAdapter(IQueueAdapter original)
    : IQueueAdapter
{
    public string Name => original.Name;
    public bool IsRewindable => original.IsRewindable;
    public StreamProviderDirection Direction => original.Direction;
    public IQueueAdapterReceiver CreateReceiver(QueueId queueId) => original.CreateReceiver(queueId);

    public Task QueueMessageBatchAsync<T>(StreamId streamId, IEnumerable<T> events,
        StreamSequenceToken? token, Dictionary<string, object>? /* KLD0041: Implement the native Orleans queue adapter request-context contract while admitting only the validated typed wakeup hint. */ requestContext)
    {
        var hint = (EventFeedHintBatchAdmission.Require(events, requestContext) /* KLD0041: Implement the native Orleans queue adapter request-context contract while admitting only the validated typed wakeup hint. */);
        return (original.QueueMessageBatchAsync(streamId, new[] { hint }, token, null!) /* KLD0041: Implement the native Orleans queue adapter request-context contract while admitting only the validated typed wakeup hint. */);
    }
}
