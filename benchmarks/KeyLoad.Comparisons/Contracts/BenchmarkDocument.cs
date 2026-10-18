using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

/// <summary>A generated corpus document and its exact-search vector.</summary>
/// <param name="Number">The numeric document position used to generate the document.</param>
/// <param name="Id">The deterministic document identifier.</param>
/// <param name="Json">The serialized document payload.</param>
/// <param name="Vector">The generated float32 vector associated with the document.</param>
public sealed record BenchmarkDocument(int Number, string Id, string Json, [property: JsonRequired] ImmutableArray<float> Vector);
