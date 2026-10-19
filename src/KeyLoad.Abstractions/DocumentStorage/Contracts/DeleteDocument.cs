namespace KeyLoad;

/// <summary>Deletes a document, optionally conditional on its expected revision.</summary>
/// <param name="Collection">Identifies the document collection.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="ExpectedRevision">Sets the optional revision precondition.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.DeleteDocument)]
public sealed record DeleteDocument([property: Orleans.Id(0)] string Collection, [property: Orleans.Id(1)] string Id, [property: Orleans.Id(2)] long? ExpectedRevision = null) : Mutation(Collection);
