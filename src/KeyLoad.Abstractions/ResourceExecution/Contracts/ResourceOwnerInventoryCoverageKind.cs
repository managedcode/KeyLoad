namespace KeyLoad;

/// <summary>Identifies conservative potential membership without claiming a nonempty-data census.</summary>
[Orleans.Alias(ResourceOwnerInventoryContract.CoverageKindAlias)]
internal enum ResourceOwnerInventoryCoverageKind : int
{
    ConservativeMayContain = 1
}
