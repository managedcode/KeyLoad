namespace KeyLoad.Comparisons.Targets;

internal static class QdrantVectorQueries
{
    internal static object Request(ReadOnlyMemory<float> query, int count, VectorIndexKind kind, VectorQueryMode mode)
        => new
        {
            query = query.ToArray(),
            limit = count,
            @params = Parameters(kind, mode),
            filter = (Filter(mode)).Value,
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
        => new
        {
            @params = Parameters(kind, mode),
            filter = (Filter(mode)).Value
        };

    private static global::ManagedCode.Communication.Result<object?> Filter(VectorQueryMode mode) => mode switch
    {
        VectorQueryMode.Plain => global::ManagedCode.Communication.Result<object?>.FailNotFound(nameof(Filter)),
        VectorQueryMode.Filtered => global::ManagedCode.Communication.Result<object?>.Succeed(((object?)(Match(QdrantVectorProtocol.Filtered)))),
        VectorQueryMode.Mixed => global::ManagedCode.Communication.Result<object?>.Succeed(((object?)(Match(QdrantVectorProtocol.Mixed)))),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static object Match(string key)
        => new
        {
            must = new[] { new { key, match = new { value = true } } }
        };
}
