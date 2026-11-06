using System.Collections.Concurrent;

namespace KeyLoad.FunctionalTests.StorageReplication;

internal sealed class StorageBatchAcknowledgementObservation(Guid commandId) : IOutgoingGrainCallFilter
{
    private readonly ConcurrentQueue<StorageCopyBatchAcknowledgement> replies = new();
    internal StorageCopyBatchAcknowledgement[] Replies => replies.ToArray();

    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        var selected = StorageBatchCallSelection.Selects(context, commandId);
        await context.Invoke();
        if (selected)
        {
            replies.Enqueue((StorageCopyBatchAcknowledgement)context.Result! /* KLD0041: The selected ISiloDatabaseService publication response has the exact generated StorageCopyBatchAcknowledgement return contract. */);
        }
    }
}
