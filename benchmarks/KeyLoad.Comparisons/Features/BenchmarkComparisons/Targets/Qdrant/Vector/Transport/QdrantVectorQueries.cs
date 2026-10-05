namespace KeyLoad.Comparisons.Targets;

internal static class QdrantVectorQueries
{
    internal static object Request(ReadOnlyMemory<float> query, int count, VectorIndexKind kind, VectorQueryMode mode)
        => new { query = query.ToArray(), limit = count, @params = Parameters(kind, mode),
            filter = Filter(mode), with_payload = new[] { "id" }, with_vector = false };

    internal static object Parameters(VectorIndexKind kind, VectorQueryMode mode)
    {
        if (kind is not (VectorIndexKind.Exact or VectorIndexKind.Hnsw) || !Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(kind));
        return new { exact = kind == VectorIndexKind.Exact, hnsw_ef = 200,
            indexed_only = kind == VectorIndexKind.Hnsw };
    }

    private static object? Filter(VectorQueryMode mode) => mode switch
    {
        VectorQueryMode.Plain => null,
        VectorQueryMode.Filtered => Match("filtered"),
        VectorQueryMode.Mixed => Match("mixed"),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static object Match(string key)
        => new { must = new[] { new { key, match = new { value = true } } } };
}
