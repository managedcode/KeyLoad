using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class QdrantVectorIndexValidation
{
    internal static bool Ready(JsonElement result, VectorComparisonProfile profile)
    {
        var config = result.GetProperty("config");
        var vectors = config.GetProperty("params").GetProperty("vectors");
        if (vectors.GetProperty("size").GetInt32() != profile.Dimensions
            || vectors.GetProperty("distance").GetString() != "Cosine")
            throw new ComparisonFailureException("QdrantVectorNativeMetricMismatch");
        var hnsw = config.GetProperty("hnsw_config");
        if (profile.IndexKind == VectorIndexKind.Exact)
        {
            if (hnsw.GetProperty("m").GetInt32() != 0 || result.GetProperty("indexed_vectors_count").GetInt64() != 0)
                throw new ComparisonFailureException("QdrantExactNativeIndexMismatch");
        }
        else if (hnsw.GetProperty("m").GetInt32() != 16 || hnsw.GetProperty("ef_construct").GetInt32() != 200
            || hnsw.GetProperty("full_scan_threshold").GetInt32() != 0)
            throw new ComparisonFailureException("QdrantHnswNativeIndexMismatch");
        return result.GetProperty("status").GetString() == "green"
            && result.GetProperty("optimizer_status").ValueKind == JsonValueKind.String
            && result.GetProperty("optimizer_status").GetString() == "ok"
            && result.GetProperty("points_count").GetInt64() == profile.RecordCount
            && (profile.IndexKind == VectorIndexKind.Exact
                || result.GetProperty("indexed_vectors_count").GetInt64() >= profile.RecordCount);
    }
}
