using System.Collections.Immutable;

namespace KeyLoad.Query.Features.QueryExecution;

[GenerateSerializer]
[Alias(PartitionQuerySerializationAliases.LeafResult)]
internal sealed record PartitionQueryLeafResultV1(
    [property: Id(0)] int Version,
    [property: Id(1)] PartitionRef Partition,
    [property: Id(2)] Guid NodeId,
    [property: Id(3)] Guid Incarnation,
    [property: Id(4)] long ReadGeneration,
    [property: Id(5)] long CutPosition,
    [property: Id(6)] long PolicyEpoch,
    [property: Id(7)] long SchemaVersion,
    [property: Id(8)] int ExaminedRecords,
    [property: Id(9)] long ReadBytes,
    [property: Id(10)] long RetainedBytes,
    [property: Id(11)] string AccessPath,
    [property: Id(12)] ImmutableArray<PartitionQueryCandidateV1> Candidates);
