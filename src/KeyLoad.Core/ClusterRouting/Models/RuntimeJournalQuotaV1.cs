using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Models;

[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalProtocol.QuotaAlias)]
internal sealed record RuntimeJournalQuotaV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] int JournalCount,
    [property: Orleans.Id(2)] long TotalBytes);
