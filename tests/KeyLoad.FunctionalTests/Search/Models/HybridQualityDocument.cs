using System.Collections.Immutable;

namespace KeyLoad.FunctionalTests.Search;

internal sealed record HybridQualityDocument(string Id, string Json, ImmutableArray<float> Embedding);
