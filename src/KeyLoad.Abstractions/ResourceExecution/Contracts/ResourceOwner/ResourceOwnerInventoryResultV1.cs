using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Returns a conservative may-contain inventory only after all owners and source witnesses validate.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceOwnerInventoryContract.ResourceOwnerInventoryResultV1Alias)]
internal sealed record ResourceOwnerInventoryResultV1(
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryResultV1ControlField)] ResourceOwnerInventoryControlV1 Control,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryResultV1OwnerCutsField)] ImmutableArray<ResourceOwnerInventoryOwnerCutV1> OwnerCuts,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryResultV1EntriesField)] ImmutableArray<ResourceOwnerInventoryEntryV1> Entries,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryResultV1CompleteField)] bool Complete,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryResultV1CoverageKindField)] ResourceOwnerInventoryCoverageKind CoverageKind);
