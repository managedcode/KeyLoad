namespace KeyLoad.Comparisons.Targets;

internal static class QdrantCollectionRequests
{
    internal static object Create(int count)
        => new
        {
            vectors = new
            {
                size = QdrantVectorProtocol.VectorDimensions,
                distance = QdrantVectorProtocol.CosineMetric
            },
            shard_number = QdrantVectorProtocol.SingleShard,
            replication_factor = count,
            write_consistency_factor = count / QdrantVectorProtocol.QuorumDivisor + QdrantVectorProtocol.MajorityIncrement,
            hnsw_config = new
            {
                m = QdrantVectorProtocol.DisabledIndex
            },
            optimizers_config = new
            {
                indexing_threshold = QdrantVectorProtocol.DisabledIndex
            }
        };
}
