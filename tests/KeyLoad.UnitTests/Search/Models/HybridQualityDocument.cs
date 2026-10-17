using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed record HybridQualityDocument(string Id, string Json, ImmutableArray<float> Embedding);
