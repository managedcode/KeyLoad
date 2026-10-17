using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Returns one bounded owner page without asserting global completeness.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceOwnerInventoryContract.ResourceOwnerInventoryPageV1Alias)]
internal sealed record ResourceOwnerInventoryPageV1(
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryPageV1CutField)] ResourceOwnerInventoryOwnerCutV1 Cut,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryPageV1EntriesField)] ImmutableArray<ResourceOwnerInventoryEntryV1> Entries,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryPageV1ContinuationField)] ResourceOwnerInventoryCursorV1? Continuation);
