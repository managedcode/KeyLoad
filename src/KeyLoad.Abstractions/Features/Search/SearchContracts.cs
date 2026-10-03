using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Selects the distance function used to compare vectors.</summary>
public enum DistanceMetric
{
    /// <summary>Compare vectors using cosine distance.</summary>
    Cosine,
    /// <summary>Compare vectors using Euclidean distance.</summary>
    Euclidean,
    /// <summary>Compare vectors using dot product.</summary>
    DotProduct
}

/// <summary>Defines the dimensionality, model, and metric of a vector space.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Dimension">Defines the vector dimension.</param>
/// <param name="Metric">Selects the vector distance metric.</param>
/// <param name="Model">Identifies the embedding model.</param>
/// <param name="Version">Identifies the embedding model version.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.VectorSpace)]
public sealed record VectorSpace([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] int Dimension, [property: Orleans.Id(2)] DistanceMetric Metric, [property: Orleans.Id(3)] string Model, [property: Orleans.Id(4)] string Version);

/// <summary>Stores a vector associated with a document field and revision.</summary>
/// <param name="Collection">Identifies the document collection.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Field">Identifies the vector field.</param>
/// <param name="Values">Contains vector components.</param>
/// <param name="Space">Defines the vector space.</param>
/// <param name="ExpectedDocumentRevision">Specifies the expected document revision value.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.PutVector)]
public sealed record PutVector([property: Orleans.Id(0)] string Collection, [property: Orleans.Id(1)] string Id, [property: Orleans.Id(2)] string Field, [property: Orleans.Id(3)] ImmutableArray<float> Values, [property: Orleans.Id(4)] VectorSpace Space,
    [property: Orleans.Id(5)] long ExpectedDocumentRevision) : Mutation(Collection);

/// <summary>Represents a stored vector associated with its document revision.</summary>
/// <param name="DocumentId">Specifies the document id value.</param>
/// <param name="Field">Identifies the vector field.</param>
/// <param name="Space">Defines the vector space.</param>
/// <param name="Values">Contains vector components.</param>
/// <param name="DocumentRevision">Identifies the associated document revision.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.VectorRecord)]
public sealed record VectorRecord([property: Orleans.Id(0)] string DocumentId, [property: Orleans.Id(1)] string Field, [property: Orleans.Id(2)] VectorSpace Space, [property: Orleans.Id(3)] ImmutableArray<float> Values, [property: Orleans.Id(4)] long DocumentRevision);

/// <summary>Pairs a redacted document result with its search score.</summary>
/// <param name="Document">Contains the ranked document result.</param>
/// <param name="Score">Contains the ranking score.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.RankedDocument)]
public sealed record RankedDocument([property: Orleans.Id(0)] DocumentResult Document, [property: Orleans.Id(1)] double Score);
