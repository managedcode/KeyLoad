namespace KeyLoad.Query.QueryExecution;

[GenerateSerializer]
[Alias(PartitionQuerySerializationAliases.LeafPlan)]
internal sealed record PartitionQueryLeafPlan(
    [property: Id(0)] int Version,
    [property: Id(1)] PartitionRef Partition,
    [property: Id(2)] AstQueryRequest Request,
    [property: Id(6)] int MaxCandidates);
