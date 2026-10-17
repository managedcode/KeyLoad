namespace KeyLoad;

/// <summary>Deletes a graph edge, optionally conditional on its revision.</summary>
/// <param name="Graph">Identifies the graph.</param>
/// <param name="EdgeId">Identifies the edge.</param>
/// <param name="ExpectedRevision">Sets the optional revision precondition.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.DeleteEdge)]
public sealed record DeleteEdge([property: Orleans.Id(0)] string Graph, [property: Orleans.Id(1)] string EdgeId, [property: Orleans.Id(2)] long? ExpectedRevision = null) : Mutation(Graph);
