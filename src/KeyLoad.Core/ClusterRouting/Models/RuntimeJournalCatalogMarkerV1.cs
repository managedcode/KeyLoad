using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Models;

[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalProtocol.CatalogMarkerAlias)]
internal sealed record RuntimeJournalCatalogMarkerV1([property: Orleans.Id(0)] int Version);
