using System.Collections.Immutable;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Query.Features.QueryExecution;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(DistributedSearchAliases.OwnedLeaf)]
internal sealed record DistributedSearchOwnedLeafV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] DistributedSearchPhase Phase,
    [property: global::Orleans.Id(2)] SearchRequest Search,
    [property: global::Orleans.Id(3)] PhysicalShardRecord Owner,
    [property: global::Orleans.Id(4)] string Tenant,
    [property: global::Orleans.Id(5)] long MaxReadBytes,
    [property: global::Orleans.Id(6)] int MaxExaminedRecords,
    [property: global::Orleans.Id(7)] int MaxResultBytes,
    [property: global::Orleans.Id(8)] DistributedTextWitnessV1? Witness,
    [property: global::Orleans.Id(9)] DistributedTextStatisticsV1? Statistics,
    [property: global::Orleans.Id(10)] GlobalBranchScope? Scope,
    [property: global::Orleans.Id(11)] string? SourceWindowId,
    [property: global::Orleans.Id(12)] ImmutableArray<GlobalBranchCandidate> Selected);
