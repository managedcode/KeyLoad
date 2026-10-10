using System.Collections.Immutable;

namespace KeyLoad.Query.Features.QueryExecution;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(DistributedSearchAliases.EpochInput)]
internal sealed record DistributedSearchEpochInputV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] string RequestDigest,
    [property: global::Orleans.Id(2)] ImmutableArray<DistributedTextWitnessV1> Witnesses);
