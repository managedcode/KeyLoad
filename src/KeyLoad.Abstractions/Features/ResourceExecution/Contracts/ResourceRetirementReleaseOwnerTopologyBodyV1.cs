namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementReleaseOwnerTopologyBodyAlias)]
internal sealed record ResourceRetirementReleaseOwnerTopologyBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementFinalizationV1 Finalization,
    [property: Orleans.Id(1)] RegisteredPhysicalOwnerV1 Receiver);
