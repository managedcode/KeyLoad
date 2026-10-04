using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Describes a combined text and vector search.</summary>
/// <param name="Partition">The partition to search.</param>
/// <param name="Collection">The document collection to search.</param>
/// <param name="TextField">The optional text field to search.</param>
/// <param name="Text">The optional text query.</param>
/// <param name="VectorField">The optional vector field to search.</param>
/// <param name="Vector">The optional query vector.</param>
/// <param name="Space">The vector space used for vector similarity.</param>
/// <param name="Limit">The maximum number of results to return.</param>
/// <param name="TextWeight">The ranking weight assigned to text results.</param>
/// <param name="VectorWeight">The ranking weight assigned to vector results.</param>
/// <param name="FusionConstant">The rank-fusion constant used when combining result lists.</param>
/// <param name="AllowedIds">Optional canonical document IDs eligible for branch output; an empty array selects no results.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SearchRequest)]
public sealed record SearchRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Collection, [property: Orleans.Id(2)] string? TextField = null, [property: Orleans.Id(3)] string? Text = null,
    [property: Orleans.Id(4)] string? VectorField = null, [property: Orleans.Id(5)] ImmutableArray<float>? Vector = null, [property: Orleans.Id(6)] VectorSpace? Space = null, [property: Orleans.Id(7)] int Limit = 10,
    [property: Orleans.Id(8)] double TextWeight = 1, [property: Orleans.Id(9)] double VectorWeight = 1, [property: Orleans.Id(10)] int FusionConstant = 60,
    [property: Orleans.Id(11)] ImmutableArray<string>? AllowedIds = null);
