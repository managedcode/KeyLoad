using System.Collections.Immutable;

namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementWorkOwnerReceiptAlias)]
internal sealed record ResourceRetirementWorkOwnerReceiptV1(
    [property: Orleans.Id(0)] ResourceRetirementControlV1 Control,
    [property: Orleans.Id(1)] RegisteredPhysicalOwnerV1 Receiver,
    [property: Orleans.Id(2)] ResourceRetirementOwnerFenceV1 Fence,
    [property: Orleans.Id(3)] ImmutableArray<ResourceRetirementWorkJoinEvidenceV1> Evidence,
    [property: Orleans.Id(4)] ResourceRetirementPhaseReceiptV1 Receipt);
