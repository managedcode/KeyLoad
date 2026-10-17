using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>One canonical search shape over a bounded set of complete atomic partitions.</summary>
/// <param name="Version">The admitted distributed search version.</param>
/// <param name="Partitions">Unique logical scopes, beginning with the search template partition.</param>
/// <param name="Search">Existing canonical lexical/vector search shape, with no caller authority.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(DistributedSearchContractAliases.Request)]
public sealed record DistributedSearchRequestV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] ImmutableArray<PartitionRef> Partitions,
    [property: global::Orleans.Id(2)] SearchRequest Search);
