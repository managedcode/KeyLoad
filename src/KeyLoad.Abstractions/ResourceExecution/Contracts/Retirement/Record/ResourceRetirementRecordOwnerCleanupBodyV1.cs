namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementRecordOwnerCleanupBodyAlias)]
internal sealed record ResourceRetirementRecordOwnerCleanupBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementControlV1 Control,
    [property: Orleans.Id(1)] ResourceRetirementOwnerCleanupV1 OwnerCleanup);
