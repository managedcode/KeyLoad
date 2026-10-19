using KeyLoad.Core.Features.RuntimeJournaling.Contracts;

namespace KeyLoad.Core.Features.RuntimeJournaling.Models;

[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalProtocol.QuotaAlias)]
internal sealed record RuntimeJournalQuota(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] int JournalCount,
    [property: Orleans.Id(2)] long TotalBytes);
