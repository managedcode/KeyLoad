using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class QdrantVectorIndexValidation
{
    private const string CosineMetric = QdrantVectorProtocol.CosineMetric;
    private const string ReadyIndexStatus = QdrantVectorProtocol.ReadyStatus;
    private const string ReadyOptimizerStatus = QdrantVectorProtocol.ReadyOptimizer;

    internal static bool Ready(JsonElement result, VectorComparisonProfile profile)
    {
        var config = result.GetProperty(QdrantVectorProtocol.Configuration);
        var vectors = config.GetProperty(QdrantVectorProtocol.Parameters).GetProperty(QdrantVectorProtocol.Vectors);
        if (vectors.GetProperty(QdrantVectorProtocol.Size).GetInt32() != profile.Dimensions
            || vectors.GetProperty(QdrantVectorProtocol.Distance).GetString() != CosineMetric)
        {
            throw new ComparisonFailureException(QdrantVectorProtocol.MetricMismatch);
        }
        var hnsw = config.GetProperty(QdrantVectorProtocol.HnswConfiguration);
        if (profile.IndexKind == VectorIndexKind.Exact)
        {
            if (hnsw.GetProperty(QdrantVectorProtocol.Connections).GetInt32() != QdrantVectorProtocol.EmptyCount || result.GetProperty(QdrantVectorProtocol.IndexedVectorCount).GetInt64() != QdrantVectorProtocol.EmptyCount)
            {
                throw new ComparisonFailureException(QdrantVectorProtocol.ExactMismatch);
            }
        }
        else if (hnsw.GetProperty(QdrantVectorProtocol.Connections).GetInt32() != QdrantVectorProtocol.HnswConnections || hnsw.GetProperty(QdrantVectorProtocol.ConstructionBreadth).GetInt32() != QdrantVectorProtocol.HnswBreadth
            || hnsw.GetProperty(QdrantVectorProtocol.FullScanThreshold).GetInt32() != QdrantVectorProtocol.EmptyCount)
        {
            throw new ComparisonFailureException(QdrantVectorProtocol.HnswMismatch);
        }
        return result.GetProperty(QdrantVectorProtocol.Status).GetString() == ReadyIndexStatus
            && result.GetProperty(QdrantVectorProtocol.OptimizerStatus).ValueKind == JsonValueKind.String
            && result.GetProperty(QdrantVectorProtocol.OptimizerStatus).GetString() == ReadyOptimizerStatus
            && result.GetProperty(QdrantVectorProtocol.PointCount).GetInt64() == profile.RecordCount
            && (profile.IndexKind == VectorIndexKind.Exact
                || result.GetProperty(QdrantVectorProtocol.IndexedVectorCount).GetInt64() >= profile.RecordCount);
    }
}
