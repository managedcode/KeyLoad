using System.Collections.Immutable;

namespace KeyLoad.Query.Features.QueryExecution;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(DistributedSearchAliases.TextStatistics)]
internal sealed record DistributedTextStatisticsV1(
    [property: global::Orleans.Id(0)] ImmutableArray<string> Terms,
    [property: global::Orleans.Id(1)] int DocumentCount,
    [property: global::Orleans.Id(2)] long TotalLength,
    [property: global::Orleans.Id(3)] ImmutableArray<int> DocumentFrequencies);
