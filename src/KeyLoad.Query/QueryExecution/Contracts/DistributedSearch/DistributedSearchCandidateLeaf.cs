using KeyLoad.Query.Search;

namespace KeyLoad.Query.QueryExecution;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(DistributedSearchAliases.CandidateLeaf)]
internal sealed record DistributedSearchCandidateLeaf(
    [property: global::Orleans.Id(0)] DistributedTextWitness Witness,
    [property: global::Orleans.Id(1)] GlobalBranchWindow? Text,
    [property: global::Orleans.Id(2)] GlobalBranchWindow? Vector);
