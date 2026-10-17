namespace KeyLoad;

/// <summary>Locates the current source intent; payload and revision are reloaded from storage.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionAliases.ApplyReverseEdge)]
public sealed record ApplyCrossPartitionReverseEdge(
    [property: Orleans.Id(GraphCrossPartitionFields.MutationSourcePartition)] PartitionRef SourcePartition,
    [property: Orleans.Id(GraphCrossPartitionFields.MutationGraph)] string Graph,
    [property: Orleans.Id(GraphCrossPartitionFields.MutationEdgeId)] string EdgeId,
    [property: Orleans.Id(GraphCrossPartitionFields.MutationDestination)] EntityRef Destination,
    [property: Orleans.Id(GraphCrossPartitionFields.MutationExpectedRevision)] long ExpectedRevision) : Mutation(Graph);
