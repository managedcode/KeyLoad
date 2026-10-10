using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal EventVectorStreamSetCapture ReadEventVectorStreamSetCapture(IKeyValueView originalView,
        string principalId, PartitionRef control, PartitionRef sourcePartition, string streamSet,
        PhysicalShardRecord configuredOwner, ReadExecutionBudget originalBudget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalView);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(sourcePartition);
        ArgumentNullException.ThrowIfNull(configuredOwner);
        ArgumentNullException.ThrowIfNull(originalBudget);
        originalBudget.Check();
        cancellationToken.ThrowIfCancellationRequested();
        var bounded = originalBudget.CreateView(originalView);
        var principal = Principal(bounded, principalId, Clock.GetUtcNow());
        Authorization.Require(principal, control, streamSet, Capability.SubscriptionsManage);
        Authorization.Require(principal, sourcePartition, streamSet, Capability.EventsRead);
        var placement = StreamTraversalPlacement(bounded, sourcePartition, configuredOwner);
        var resource = Resource(bounded, sourcePartition, streamSet, ResourceKind.StreamSet);
        var inventory = new EventVectorInventoryReadBudget(OperationLimitsOptions);
        var heads = EventVectorStreamHeadsRead.Read(bounded, sourcePartition, streamSet, inventory,
            cancellationToken);
        foreach (var captured in heads)
        {
            originalBudget.Check();
            _ = SourceResource(bounded, new EventSourceRef(sourcePartition, streamSet,
                EventSourceKind.Stream, captured.StreamId, captured.Head.Generation));
        }
        originalBudget.Check();
        return new(resource, placement, heads);
    }
}
