using KeyLoad.Core.GraphTraversal.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes GraphTraversalValidation operations against the caller's database context.</summary>
public static class GraphTraversalValidation
{
    internal const string GraphDeliveryPartitionMismatch = "The graph delivery locator does not match its command partition.";
    internal static void ReauthorizeGraphDelivery(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation mutation)
    {
        var (source, graph, edgeId, destination, revision, expectedPartition) = mutation switch
        {
            ApplyCrossPartitionReverseEdge apply => (apply.SourcePartition, apply.Graph, apply.EdgeId, apply.Destination, apply.ExpectedRevision, apply.Destination.Partition),
            CompleteCrossPartitionReverseEdge complete => (complete.SourcePartition, complete.Graph, complete.EdgeId, complete.Destination, complete.ExpectedRevision, complete.SourcePartition),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, AtomicMutationApplication.UnsupportedMutationMessage)
        };
        GraphCrossPartitionValidation.ValidateLocator(source, graph, edgeId, destination, revision);
        if (partition != expectedPartition || source == destination.Partition)
        {
            throw Errors.Fail(ErrorCode.Validation, GraphDeliveryPartitionMismatch);
        }

        engine.RequireGraphWrite(view, principal, source, destination.Partition, graph);
        var intent = GraphTraversalCommands.CurrentIntent(view, source, graph, edgeId, destination);
        if (intent is { Deleted: false })
        {
            engine.VisibleVertex(view, principal, intent.Edge.From);
            engine.VisibleVertex(view, principal, intent.Edge.To);
        }
        else if (intent is null && view.GetRecord<EdgeRecord>(GraphTraversalStorage.EdgeKey(source, graph, edgeId)) is { } canonical && (canonical.To with
        {
            Lifetime = null
        }

        ) == (destination with
        {
            Lifetime = null
        }

        ))
        {
            engine.VisibleVertex(view, principal, canonical.From);
            engine.VisibleVertex(view, principal, canonical.To);
            _ = engine.BindEntityReference(view, destination);
        }
    }
}
