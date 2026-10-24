namespace KeyLoad.Query.QueryExecution;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(DistributedSearchAliases.LeafResult)]
internal sealed record DistributedSearchLeafResult(
    [property: global::Orleans.Id(0)] DistributedSearchPhase Phase,
    [property: global::Orleans.Id(1)] DistributedTextWitness Witness,
    [property: global::Orleans.Id(2)] DistributedSearchCandidateLeaf? Candidates,
    [property: global::Orleans.Id(3)] DistributedSearchProjectionLeaf? Projection,
    [property: global::Orleans.Id(4)] long ReadBytes,
    [property: global::Orleans.Id(5)] int ExaminedRecords);
