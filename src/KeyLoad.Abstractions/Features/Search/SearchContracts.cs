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
public sealed record VectorSpace(string Id, int Dimension, DistanceMetric Metric, string Model, string Version);

/// <summary>Stores a vector associated with a document field and revision.</summary>
/// <param name="Collection">Identifies the document collection.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Field">Identifies the vector field.</param>
/// <param name="Values">Contains vector components.</param>
/// <param name="Space">Defines the vector space.</param>
/// <param name="ExpectedDocumentRevision">Specifies the expected document revision value.</param>
public sealed record PutVector(string Collection, string Id, string Field, ImmutableArray<float> Values, VectorSpace Space,
    long ExpectedDocumentRevision) : Mutation(Collection);

/// <summary>Represents a stored vector associated with its document revision.</summary>
/// <param name="DocumentId">Specifies the document id value.</param>
/// <param name="Field">Identifies the vector field.</param>
/// <param name="Space">Defines the vector space.</param>
/// <param name="Values">Contains vector components.</param>
/// <param name="DocumentRevision">Identifies the associated document revision.</param>
public sealed record VectorRecord(string DocumentId, string Field, VectorSpace Space, ImmutableArray<float> Values, long DocumentRevision);

/// <summary>Pairs a redacted document result with its search score.</summary>
/// <param name="Document">Contains the ranked document result.</param>
/// <param name="Score">Contains the ranking score.</param>
public sealed record RankedDocument(DocumentResult Document, double Score);
