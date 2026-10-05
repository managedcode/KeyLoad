namespace KeyLoad.Comparisons.Targets;

internal static class QdrantVectorQueries
{
    internal static object Request(ReadOnlyMemory<float> query, int count, VectorIndexKind kind, VectorQueryMode mode)
        => new
        {
            query = query.ToArray(),
            limit = count,
            @params = Parameters(kind, mode),
            filter = Filter(mode),
            with_payload = new[] { QdrantVectorProtocol.Identifier },
            with_vector = false
        };

    internal static object Parameters(VectorIndexKind kind, VectorQueryMode mode)
    {
        if (kind is not (VectorIndexKind.Exact or VectorIndexKind.Hnsw) || !Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        return new
        {
            exact = kind == VectorIndexKind.Exact,
            hnsw_ef = QdrantVectorProtocol.HnswBreadth,
            indexed_only = kind == VectorIndexKind.Hnsw
        };
    }

    internal static object Evidence(VectorIndexKind kind, VectorQueryMode mode)
        => new { @params = Parameters(kind, mode), filter = Filter(mode) };

    private static object? Filter(VectorQueryMode mode) => mode switch
    {
        VectorQueryMode.Plain => null,
        VectorQueryMode.Filtered => Match(QdrantVectorProtocol.Filtered),
        VectorQueryMode.Mixed => Match(QdrantVectorProtocol.Mixed),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static object Match(string key)
        => new { must = new[] { new { key, match = new { value = true } } } };
}
