using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Returns a bounded visible metadata page and an exclusive progress cursor.</summary>
/// <param name="Items">The authorized live metadata rows.</param>
/// <param name="NextAfterId">The last visited ID, or null after exhaustion.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobListPage)]
public sealed record BlobListPage([property: Orleans.Id(0)] ImmutableArray<BlobMetadata> Items, [property: Orleans.Id(1)] string? NextAfterId);
