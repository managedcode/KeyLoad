using System.Collections.Immutable;

namespace KeyLoad.Query.QueryExecution;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(DistributedTextStatisticsAliases.Statistics)]
internal sealed record DistributedTextStatistics(
    [property: global::Orleans.Id(0)] ImmutableArray<string> Terms,
    [property: global::Orleans.Id(1)] int DocumentCount,
    [property: global::Orleans.Id(2)] long TotalLength,
    [property: global::Orleans.Id(3)] ImmutableArray<int> DocumentFrequencies);
