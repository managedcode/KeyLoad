namespace KeyLoad;

/// <summary>Creates or replaces a graph edge, optionally conditional on its revision.</summary>
/// <param name="Graph">Identifies the graph.</param>
/// <param name="EdgeId">Identifies the edge.</param>
/// <param name="From">Identifies the source endpoint.</param>
/// <param name="To">Identifies the target endpoint.</param>
/// <param name="Label">Names the edge label.</param>
/// <param name="AttributesJson">Contains the edge attributes as JSON.</param>
/// <param name="ExpectedRevision">Sets the optional revision precondition.</param>
public sealed record UpsertEdge(string Graph, string EdgeId, EntityRef From, EntityRef To, string Label,
    string AttributesJson = "{}", long? ExpectedRevision = null) : Mutation(Graph);

/// <summary>Deletes a graph edge, optionally conditional on its revision.</summary>
/// <param name="Graph">Identifies the graph.</param>
/// <param name="EdgeId">Identifies the edge.</param>
/// <param name="ExpectedRevision">Sets the optional revision precondition.</param>
public sealed record DeleteEdge(string Graph, string EdgeId, long? ExpectedRevision = null) : Mutation(Graph);

/// <summary>Represents a stored graph edge and its current revision.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="From">Identifies the source endpoint.</param>
/// <param name="To">Identifies the target endpoint.</param>
/// <param name="Label">Names the edge label.</param>
/// <param name="AttributesJson">Contains the edge attributes as JSON.</param>
/// <param name="Revision">Identifies the document, stream, or edge revision.</param>
public sealed record EdgeRecord(string Id, EntityRef From, EntityRef To, string Label, string AttributesJson, long Revision);
