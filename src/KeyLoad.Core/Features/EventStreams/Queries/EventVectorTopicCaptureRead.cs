using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal EventVectorTopicCapture ReadEventVectorTopicCapture(IKeyValueView originalView,
        string principalId, PartitionRef control, PartitionRef sourcePartition, string topic,
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
        Authorization.Require(principal, control, topic, Capability.SubscriptionsManage);
        Authorization.Require(principal, sourcePartition, topic, Capability.TopicsRead);
        var placement = StreamTraversalPlacement(bounded, sourcePartition, configuredOwner);
        var resource = Resource(bounded, sourcePartition, topic, ResourceKind.Topic);
        var inventory = new EventVectorInventoryReadBudget(OperationLimitsOptions);
        var captured = EventVectorTopicHeadRead.Read(bounded, sourcePartition, topic, inventory,
            cancellationToken);
        if (captured.Head is { } actualHead)
        {
            _ = SourceResource(bounded, new EventSourceRef(sourcePartition, topic,
                EventSourceKind.Topic, Generation: actualHead.Generation));
        }
        originalBudget.Check();
        return new(resource, placement, captured.Head, captured.Row);
    }
}
