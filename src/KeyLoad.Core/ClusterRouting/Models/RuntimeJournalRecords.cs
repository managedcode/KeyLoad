using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Models;

[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalProtocol.HeaderAlias)]
internal sealed record RuntimeJournalHeaderV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] string Name,
    [property: Orleans.Id(2)] Guid InstanceId,
    [property: Orleans.Id(3)] long OwnerGeneration,
    [property: Orleans.Id(4)] long ContentRevision,
    [property: Orleans.Id(5)] long Length,
    [property: Orleans.Id(6)] string MetadataETag,
    [property: Orleans.Id(7)] Dictionary<string, string> Properties);

[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalProtocol.ChunkAlias)]
internal sealed record RuntimeJournalChunkV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] int Index,
    [property: Orleans.Id(2)] byte[] Data);

[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalProtocol.CatalogMarkerAlias)]
internal sealed record RuntimeJournalCatalogMarkerV1([property: Orleans.Id(0)] int Version);

[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalProtocol.QuotaAlias)]
internal sealed record RuntimeJournalQuotaV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] int JournalCount,
    [property: Orleans.Id(2)] long TotalBytes);
