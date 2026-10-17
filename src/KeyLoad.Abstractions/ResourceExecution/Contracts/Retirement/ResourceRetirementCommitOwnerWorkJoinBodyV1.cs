using System.Collections.Immutable;

namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementCommitOwnerWorkJoinBodyAlias)]
internal sealed record ResourceRetirementCommitOwnerWorkJoinBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementStateV1 SealedState,
    [property: Orleans.Id(1)] RegisteredPhysicalOwnerV1 Receiver,
    [property: Orleans.Id(2)] ImmutableArray<ResourceRetirementWorkJoinEvidenceV1> Evidence);
