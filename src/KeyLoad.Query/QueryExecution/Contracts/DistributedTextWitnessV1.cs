namespace KeyLoad.Query.Features.QueryExecution;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(DistributedSearchAliases.TextWitness)]
internal sealed record DistributedTextWitnessV1(
    [property: global::Orleans.Id(0)] PartitionRef Partition,
    [property: global::Orleans.Id(1)] PhysicalShardRecord Owner,
    [property: global::Orleans.Id(2)] Guid NodeId,
    [property: global::Orleans.Id(3)] long ReadGeneration,
    [property: global::Orleans.Id(4)] long CutPosition,
    [property: global::Orleans.Id(5)] long PolicyEpoch,
    [property: global::Orleans.Id(6)] long SchemaVersion,
    [property: global::Orleans.Id(7)] string ResourcePolicyDigest,
    [property: global::Orleans.Id(8)] DistributedTextStatisticsV1 Statistics,
    [property: global::Orleans.Id(9)] long ReadBytes,
    [property: global::Orleans.Id(10)] int ExaminedRecords,
    [property: global::Orleans.Id(11)] Guid Incarnation);
