using System.Collections.Concurrent;
using System.Collections.Immutable;
using KeyLoad.Clustering;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.StorageReplication;

internal sealed class StorageBatchAcknowledgementObservation(Guid commandId) : IOutgoingGrainCallFilter
{
    private readonly ConcurrentQueue<StorageCopyBatchAcknowledgement> replies = new();
    internal StorageCopyBatchAcknowledgement[] Replies => replies.ToArray();

    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        var selected = context.InterfaceMethod.DeclaringType == typeof(ISiloDatabaseService)
            && context.MethodName == nameof(ISiloDatabaseService.ApplyBatchAsync)
            && context.Request.GetArgument(0) is ImmutableArray<Command<DatabaseCommand>> operations
            && operations.Any(operation => operation.CommandId == commandId);
        await context.Invoke();
        if (selected)
        {
            replies.Enqueue((StorageCopyBatchAcknowledgement)context.Result! /* KLD0041: The selected ISiloDatabaseService.ApplyBatchAsync response has the exact generated StorageCopyBatchAcknowledgement return contract. */);
        }
    }
}
