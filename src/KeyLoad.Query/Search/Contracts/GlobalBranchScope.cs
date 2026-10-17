namespace KeyLoad.Query.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(GlobalBranchProtocol.ScopeAlias)]
internal sealed record GlobalBranchScope(
    [property: global::Orleans.Id(0)] string ScoreProfile,
    [property: global::Orleans.Id(1)] string CorpusScope,
    [property: global::Orleans.Id(2)] string StatisticsEpoch);
