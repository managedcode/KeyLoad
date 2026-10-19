using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Stores a vector associated with a document field and revision.</summary>
/// <param name="Collection">Identifies the document collection.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Field">Identifies the vector field.</param>
/// <param name="Values">Contains vector components.</param>
/// <param name="Space">Defines the vector space.</param>
/// <param name="ExpectedDocumentRevision">Specifies the expected document revision value.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.PutVector)]
public sealed record PutVector([property: Orleans.Id(0)] string Collection, [property: Orleans.Id(1)] string Id, [property: Orleans.Id(2)] string Field, [property: Orleans.Id(3)] ImmutableArray<float> Values, [property: Orleans.Id(4)] VectorSpace Space,
    [property: Orleans.Id(5)] long ExpectedDocumentRevision) : Mutation(Collection);
