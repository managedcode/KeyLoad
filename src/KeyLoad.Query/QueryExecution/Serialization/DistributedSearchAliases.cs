namespace KeyLoad.Query.QueryExecution;

internal static class DistributedSearchAliases
{
    internal const string Phase = "keyload.query.distributed-search-phase";
    internal const string OwnedLeaf = "keyload.query.distributed-search-owned-leaf";
    internal const string LeafResult = "keyload.query.distributed-search-leaf-result";
    internal const string TextStatistics = DistributedTextStatisticsAliases.Statistics;
    internal const string TextWitness = "keyload.query.distributed-text-witness";
    internal const string EpochInput = "keyload.query.distributed-search-epoch-input";
    internal const string ProjectionLeaf = "keyload.query.distributed-search-projection-leaf";
    internal const string CandidateLeaf = "keyload.query.distributed-search-candidate-leaf";
}
