namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementOwnerFenceAlias)]
internal sealed record ResourceRetirementOwnerFenceV1(
    [property: Orleans.Id(0)] ResourceRetirementControlV1 Control,
    [property: Orleans.Id(1)] ResourceRetirementPhaseReceiptV1 Receipt);
