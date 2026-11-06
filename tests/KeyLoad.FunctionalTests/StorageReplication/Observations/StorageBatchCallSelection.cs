using System.Collections.Immutable;
using KeyLoad.Clustering;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.StorageReplication;

/// <summary>Selects actual primary commands or their exact persisted copy effects.</summary>
internal static class StorageBatchCallSelection
{
    internal static bool Selects(IOutgoingGrainCallContext context, Guid commandId)
    {
        if (context.InterfaceMethod.DeclaringType != typeof(ISiloDatabaseService))
        {
            return false;
        }
        if (context.MethodName == nameof(ISiloDatabaseService.CommitBatchAsync))
        {
            return context.Request.GetArgument(1) is ImmutableArray<Command<DatabaseCommand>> operations
                && operations.Any(operation => operation.CommandId == commandId);
        }
        return context.MethodName == nameof(ISiloDatabaseService.ApplyReplicaBatchAsync)
            && context.Request.GetArgument(1) is StorageReplicaBatch batch && Contains(batch, commandId);
    }

    internal static SiloAddress Target(IOutgoingGrainCallContext context)
        => SystemTargetGrainId.TryParse(context.TargetId, out var target) ? target.GetSiloAddress()
            : throw new InvalidOperationException("The storage-service transport requires its actual silo address.");

    internal static bool Contains(StorageReplicaBatch batch, Guid commandId)
    {
        if (batch.Partition is not { } partition)
        {
            return false;
        }
        var key = KeySpace.PartitionOutcome(partition, RequestCqrsHostedProfile.Root, commandId);
        return batch.Changes.Any(change => change.Key.Span.SequenceEqual(key));
    }
}
