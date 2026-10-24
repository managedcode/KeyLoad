namespace KeyLoad.Query.QueryExecution;

[GenerateSerializer]
[Alias(PartitionQuerySerializationAliases.LeafPlan)]
internal sealed record PartitionQueryLeafPlan(
    [property: Id(0)] int Version,
    [property: Id(1)] PartitionRef Partition,
    [property: Id(2)] AstQueryRequest Request,
    [property: Id(3)] int MaxExaminedRecords,
    [property: Id(4)] long MaxReadBytes,
    [property: Id(5)] long MaxRetainedBytes,
    [property: Id(6)] int MaxCandidates);
