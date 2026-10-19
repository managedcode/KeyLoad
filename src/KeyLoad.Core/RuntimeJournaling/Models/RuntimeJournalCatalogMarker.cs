using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.RuntimeJournaling.Contracts;
using KeyLoad.Core.Features.StoragePartitions.Contracts;

namespace KeyLoad.Core.Features.RuntimeJournaling.Models;

[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalProtocol.CatalogMarkerAlias)]
internal sealed record RuntimeJournalCatalogMarker([property: Orleans.Id(0)] int Version);
