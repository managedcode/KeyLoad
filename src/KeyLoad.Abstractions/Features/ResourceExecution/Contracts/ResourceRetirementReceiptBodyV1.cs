namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementReceiptBodyAlias)]
internal sealed record ResourceRetirementReceiptBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementControlV1 Control,
    [property: Orleans.Id(1)] ResourceRetirementPhaseReceiptV1 OwnerReceipt);
