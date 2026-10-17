using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Bounded catalog page from one gated quorum-established read cut.</summary>
/// <param name="Items">Resource metadata in canonical key order.</param>
/// <param name="NextAfterName">Exclusive resource continuation, null on exhaustion.</param>
/// <param name="CutPosition">Applied position of the gated read cut.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AdminResourcesPage)]
public sealed record AdminResourcesPage([property: Orleans.Id(0)] ImmutableArray<AdminResourceInfo> Items, [property: Orleans.Id(1)] string? NextAfterName, [property: Orleans.Id(2)] long CutPosition);
