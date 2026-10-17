using System.Collections.Immutable;

namespace KeyLoad.Clustering;

internal sealed record NativeResourceWorkClosureReceipt(ResourceRetirementOwnerFenceV1 Fence,
    PhysicalShardRecord ActualOwner, ImmutableArray<NativeResourceWorkSnapshot> Admissions,
    ImmutableArray<NativeRequestWorkOriginalSettlement> OriginalOutcomes);
