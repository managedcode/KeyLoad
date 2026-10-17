using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Models;

[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalProtocol.ChunkAlias)]
internal sealed record RuntimeJournalChunkV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] int Index,
    [property: Orleans.Id(2)] byte[] Data);
