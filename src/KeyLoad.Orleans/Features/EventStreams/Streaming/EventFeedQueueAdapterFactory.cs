using Orleans.Streams;

namespace KeyLoad.Orleans;

internal sealed class EventFeedQueueAdapterFactory(
    IQueueAdapterFactory original, IQueueAdapterCache cache, EventFeedHintBatchAdmission admission) : IQueueAdapterFactory
{
    private const string UseCancellationOverload = "Use the overload which accepts a CancellationToken.";

    [Obsolete(UseCancellationOverload)]
    public Task<IQueueAdapter> CreateAdapter() => CreateAdapter(CancellationToken.None);

    public async Task<IQueueAdapter> CreateAdapter(CancellationToken cancellationToken)
    {
        var adapter = await original.CreateAdapter(cancellationToken).ConfigureAwait(false);
        return new EventFeedQueueAdapter(adapter, admission);
    }

    public IQueueAdapterCache GetQueueAdapterCache() => cache;

    public IStreamQueueMapper GetStreamQueueMapper() => original.GetStreamQueueMapper();

    public Task<IStreamFailureHandler> GetDeliveryFailureHandler(QueueId queueId)
        => original.GetDeliveryFailureHandler(queueId);
}
