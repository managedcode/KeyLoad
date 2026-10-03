namespace KeyLoad;

/// <summary>Creates or replaces a graph edge, optionally conditional on its revision.</summary>
/// <param name="Graph">Identifies the graph.</param>
/// <param name="EdgeId">Identifies the edge.</param>
/// <param name="From">Identifies the source endpoint.</param>
/// <param name="To">Identifies the target endpoint.</param>
/// <param name="Label">Names the edge label.</param>
/// <param name="AttributesJson">Contains the edge attributes as JSON.</param>
/// <param name="ExpectedRevision">Sets the optional revision precondition.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.UpsertEdge)]
public sealed record UpsertEdge([property: Orleans.Id(0)] string Graph, [property: Orleans.Id(1)] string EdgeId, [property: Orleans.Id(2)] EntityRef From, [property: Orleans.Id(3)] EntityRef To, [property: Orleans.Id(4)] string Label,
    [property: Orleans.Id(5)] string AttributesJson = "{}", [property: Orleans.Id(6)] long? ExpectedRevision = null) : Mutation(Graph);

/// <summary>Deletes a graph edge, optionally conditional on its revision.</summary>
/// <param name="Graph">Identifies the graph.</param>
/// <param name="EdgeId">Identifies the edge.</param>
/// <param name="ExpectedRevision">Sets the optional revision precondition.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.DeleteEdge)]
public sealed record DeleteEdge([property: Orleans.Id(0)] string Graph, [property: Orleans.Id(1)] string EdgeId, [property: Orleans.Id(2)] long? ExpectedRevision = null) : Mutation(Graph);

/// <summary>Represents a stored graph edge and its current revision.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="From">Identifies the source endpoint.</param>
/// <param name="To">Identifies the target endpoint.</param>
/// <param name="Label">Names the edge label.</param>
/// <param name="AttributesJson">Contains the edge attributes as JSON.</param>
/// <param name="Revision">Identifies the document, stream, or edge revision.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.EdgeRecord)]
public sealed record EdgeRecord([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] EntityRef From, [property: Orleans.Id(2)] EntityRef To, [property: Orleans.Id(3)] string Label, [property: Orleans.Id(4)] string AttributesJson, [property: Orleans.Id(5)] long Revision);
