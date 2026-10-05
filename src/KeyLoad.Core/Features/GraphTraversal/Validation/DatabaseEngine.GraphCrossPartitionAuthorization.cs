using KeyLoad.Core.Features.GraphTraversal.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string GraphDeliveryPartitionMismatch = "The graph delivery locator does not match its command partition.";

    private void ReauthorizeGraphDelivery(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, Mutation mutation)
    {
        var (source, graph, edgeId, destination, revision, expectedPartition) = mutation switch
        {
            ApplyCrossPartitionReverseEdge apply => (apply.SourcePartition, apply.Graph, apply.EdgeId,
                apply.Destination, apply.ExpectedRevision, apply.Destination.Partition),
            CompleteCrossPartitionReverseEdge complete => (complete.SourcePartition, complete.Graph,
                complete.EdgeId, complete.Destination, complete.ExpectedRevision, complete.SourcePartition),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedMutationMessage)
        };
        GraphCrossPartitionValidation.ValidateLocator(source, graph, edgeId, destination, revision);
        if (partition != expectedPartition || source == destination.Partition)
        {
            throw Errors.Fail(ErrorCode.Validation, GraphDeliveryPartitionMismatch);
        }
        RequireGraphWrite(view, principal, source, destination.Partition, graph);
        RequireSameGraphOwner(view, source, destination.Partition);
        var intent = CurrentIntent(view, source, graph, edgeId, destination, Limits);
        if (intent is { Deleted: false })
        {
            VisibleVertex(view, principal, intent.Edge.From);
            VisibleVertex(view, principal, intent.Edge.To);
        }
        else if (intent is null && view.GetRecord<EdgeRecord>(EdgeKey(source, graph, edgeId))
            is { } canonical && canonical.To == destination)
        {
            VisibleVertex(view, principal, canonical.From);
            VisibleVertex(view, principal, canonical.To);
        }
    }
}
