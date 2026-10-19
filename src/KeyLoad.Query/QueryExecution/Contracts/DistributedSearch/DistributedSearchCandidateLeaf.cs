using KeyLoad.Query.Features.Search;

namespace KeyLoad.Query.Features.QueryExecution;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(DistributedSearchAliases.CandidateLeaf)]
internal sealed record DistributedSearchCandidateLeaf(
    [property: global::Orleans.Id(0)] DistributedTextWitness Witness,
    [property: global::Orleans.Id(1)] GlobalBranchWindow? Text,
    [property: global::Orleans.Id(2)] GlobalBranchWindow? Vector,
    [property: global::Orleans.Id(3)] long ReadBytes,
    [property: global::Orleans.Id(4)] int ExaminedRecords);
