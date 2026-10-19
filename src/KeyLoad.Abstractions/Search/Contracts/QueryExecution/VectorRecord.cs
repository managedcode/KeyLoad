using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Represents a stored vector associated with its document revision.</summary>
/// <param name="DocumentId">Specifies the document id value.</param>
/// <param name="Field">Identifies the vector field.</param>
/// <param name="Space">Defines the vector space.</param>
/// <param name="Values">Contains vector components.</param>
/// <param name="DocumentRevision">Identifies the associated document revision.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.VectorRecord)]
public sealed record VectorRecord([property: Orleans.Id(0)] string DocumentId, [property: Orleans.Id(1)] string Field, [property: Orleans.Id(2)] VectorSpace Space, [property: Orleans.Id(3)] ImmutableArray<float> Values, [property: Orleans.Id(4)] long DocumentRevision);
