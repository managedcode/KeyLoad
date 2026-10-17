namespace KeyLoad.Query.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(GlobalBranchProtocol.CandidateAlias)]
internal sealed record GlobalBranchCandidate(
    [property: global::Orleans.Id(0)] EntityRef Reference,
    [property: global::Orleans.Id(1)] long Revision,
    [property: global::Orleans.Id(2)] double Score);
