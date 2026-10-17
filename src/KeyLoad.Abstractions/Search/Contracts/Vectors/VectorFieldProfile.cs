namespace KeyLoad;

/// <summary>Defines the authoritative immutable vector profile for one configured collection field.</summary>
/// <param name="Field">Canonical JSON pointer of the vector sidecar.</param>
/// <param name="Space">Complete expected vector model, version, dimension and metric identity.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(VectorFieldProfileSerializationContract.Alias)]
public sealed record VectorFieldProfile([property: Orleans.Id(0)] string Field, [property: Orleans.Id(1)] VectorSpace Space);
