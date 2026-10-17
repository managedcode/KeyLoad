namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>Frozen public operation vocabulary for the real same-owner partition-query cases.</summary>
internal static class PartitionQueryRf3Protocol
{
    internal const string Collection = "partition-query-documents";
    internal const string ToolName = "keyload_query_partitions";
    internal const string RankField = "/rank";
    internal const string RankJsonProperty = "rank";
    internal const string ValueField = "/value";
    internal const string ValueAlias = "value";
    internal const string RankIndex = "rank-order";
    internal const string DuplicateTextId = "same-id";
    internal const string NodeFailureScenario = "partition-query-leader-loss";
    internal const string AuthorizedPartition = "authorized-leaf";
    internal const string DeniedPartition = "denied-leaf";
    internal const string BoundPartition = "a-bound";
    internal const string FallbackPartition = "b-fallback";
    internal const string EmptyPartition = "c-empty";
    internal const int Version = 1;
    internal const int AstVersion = 1;
    internal const int Limit = 100;
    internal const int MaxPartitions = 8;
    internal const int MaxResults = 1_000;
    internal const int RowRevision = 1;
}
