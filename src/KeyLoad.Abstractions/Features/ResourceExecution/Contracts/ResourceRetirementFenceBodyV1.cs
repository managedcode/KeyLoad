namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementFenceBodyAlias)]
internal sealed record ResourceRetirementFenceBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementControlV1 Control,
    [property: Orleans.Id(1)] RegisteredPhysicalOwnerV1 Receiver);
