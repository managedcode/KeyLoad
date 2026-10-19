namespace KeyLoad;

/// <summary>Defines the dimensionality, model, and metric of a vector space.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Dimension">Defines the vector dimension.</param>
/// <param name="Metric">Selects the vector distance metric.</param>
/// <param name="Model">Identifies the embedding model.</param>
/// <param name="Version">Identifies the embedding model version.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.VectorSpace)]
public sealed record VectorSpace([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] int Dimension, [property: Orleans.Id(2)] DistanceMetric Metric, [property: Orleans.Id(3)] string Model, [property: Orleans.Id(4)] string Version);
