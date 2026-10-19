using KeyLoad.Core.Features.RuntimeJournaling.Contracts;

namespace KeyLoad.Core.Features.RuntimeJournaling.Models;

[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalProtocol.ChunkAlias)]
internal sealed record RuntimeJournalChunk(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] int Index,
    [property: Orleans.Id(2)] byte[] Data);
