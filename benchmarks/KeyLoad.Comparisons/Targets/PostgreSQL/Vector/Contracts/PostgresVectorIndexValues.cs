namespace KeyLoad.Comparisons.Targets;

internal static class PostgresVectorIndexValues
{
    internal const string NoANNIndexExactCosine = "No ANN index; execution exact cosine distance order";
    internal const int FirstIndex = 0;
    internal const string VectorsHnswIdx = "vectors_hnsw_idx";
    internal const string VectorsIvfflatIdx = "vectors_ivfflat_idx";
    internal const string TheRequestedPostgreSQLVectorIndexIs = "The requested PostgreSQL vector index is absent from execution metadata.";
    internal const int SingleElementOffset = 1;
    internal const char LineBreakCharacter = '\n';
    internal const string PostgreSQLPlannerDidNotSelectThe = "PostgreSQL planner did not select the requested pgvector index.";
    internal const string HnswNeighborsSetting = "16";
    internal const string HnswEffortSetting = "200";
    internal const int IvfProbeExpansionFactor = 4;
    internal const string RelaxedOrder = "relaxed_order";
    internal const string PostgreSQLDoesNotExposeTheSelected = "PostgreSQL does not expose the selected execution vector index.";
    internal const int Scale100kCount = 100_000;
}
