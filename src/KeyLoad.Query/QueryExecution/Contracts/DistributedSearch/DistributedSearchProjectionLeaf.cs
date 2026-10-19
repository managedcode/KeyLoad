using System.Collections.Immutable;

namespace KeyLoad.Query.Features.QueryExecution;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(DistributedSearchAliases.ProjectionLeaf)]
internal sealed record DistributedSearchProjectionLeaf(
    [property: global::Orleans.Id(0)] DistributedTextWitness Witness,
    [property: global::Orleans.Id(1)] ImmutableArray<RankedDocument> Hits,
    [property: global::Orleans.Id(2)] long ReadBytes,
    [property: global::Orleans.Id(3)] int ExaminedRecords);
