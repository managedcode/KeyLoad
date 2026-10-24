using KeyLoad.Core.RuntimeJournaling.Contracts;

namespace KeyLoad.Core.RuntimeJournaling.Models;

[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalProtocol.CatalogMarkerAlias)]
internal sealed record RuntimeJournalCatalogMarker([property: Orleans.Id(0)] int Version);
