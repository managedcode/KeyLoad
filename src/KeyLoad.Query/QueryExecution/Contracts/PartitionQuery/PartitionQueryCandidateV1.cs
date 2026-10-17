using System.Collections.Immutable;

namespace KeyLoad.Query.Features.QueryExecution;

[GenerateSerializer]
[Alias(PartitionQuerySerializationAliases.Candidate)]
internal sealed record PartitionQueryCandidateV1(
    [property: Id(0)] int Version,
    [property: Id(1)] EntityRef Reference,
    [property: Id(2)] QueryRow Row,
    [property: Id(3)] ImmutableArray<ImmutableArray<byte>> OrderKeys);
