namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementCompleteOwnerCleanupBodyAlias)]
internal sealed record ResourceRetirementCompleteOwnerCleanupBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementStateV1 SealedState,
    [property: Orleans.Id(1)] RegisteredPhysicalOwnerV1 Receiver);
