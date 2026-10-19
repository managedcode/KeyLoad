namespace KeyLoad;

/// <summary>Represents a stored graph edge and its current revision.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="From">Identifies the source endpoint.</param>
/// <param name="To">Identifies the target endpoint.</param>
/// <param name="Label">Names the edge label.</param>
/// <param name="AttributesJson">Contains the edge attributes as JSON.</param>
/// <param name="Revision">Identifies the document, stream, or edge revision.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.EdgeRecord)]
public sealed record EdgeRecord([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] EntityRef From, [property: Orleans.Id(2)] EntityRef To, [property: Orleans.Id(3)] string Label, [property: Orleans.Id(4)] string AttributesJson, [property: Orleans.Id(5)] long Revision);
