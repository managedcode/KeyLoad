namespace KeyLoad.Query.Features.QueryExecution;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(DistributedSearchAliases.LeafResult)]
internal sealed record DistributedSearchLeafResultV1(
    [property: global::Orleans.Id(0)] DistributedSearchPhase Phase,
    [property: global::Orleans.Id(1)] DistributedTextWitnessV1 Witness,
    [property: global::Orleans.Id(2)] DistributedSearchCandidateLeafV1? Candidates,
    [property: global::Orleans.Id(3)] DistributedSearchProjectionLeafV1? Projection,
    [property: global::Orleans.Id(4)] long ReadBytes,
    [property: global::Orleans.Id(5)] int ExaminedRecords);
