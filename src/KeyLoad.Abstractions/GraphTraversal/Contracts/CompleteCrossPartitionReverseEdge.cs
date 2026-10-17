namespace KeyLoad;

/// <summary>Completes a source intent after reloading its committed target high-water record.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionAliases.CompleteReverseEdge)]
public sealed record CompleteCrossPartitionReverseEdge(
    [property: Orleans.Id(GraphCrossPartitionFields.MutationSourcePartition)] PartitionRef SourcePartition,
    [property: Orleans.Id(GraphCrossPartitionFields.MutationGraph)] string Graph,
    [property: Orleans.Id(GraphCrossPartitionFields.MutationEdgeId)] string EdgeId,
    [property: Orleans.Id(GraphCrossPartitionFields.MutationDestination)] EntityRef Destination,
    [property: Orleans.Id(GraphCrossPartitionFields.MutationExpectedRevision)] long ExpectedRevision) : Mutation(Graph);
