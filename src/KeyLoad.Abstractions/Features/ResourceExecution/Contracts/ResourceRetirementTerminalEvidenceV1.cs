using System.Collections.Immutable;

namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementTerminalEvidenceAlias)]
internal sealed record ResourceRetirementTerminalEvidenceV1(
    [property: Orleans.Id(0)] ResourceRetirementStateV1 SealedState,
    [property: Orleans.Id(1)] ImmutableArray<ResourceRetirementOwnerCleanupV1> Owners);
