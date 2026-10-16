namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementPhaseReceiptAlias)]
internal sealed record ResourceRetirementPhaseReceiptV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid RetirementId,
    [property: Orleans.Id(2)] ResourceRetirementPhase Phase,
    [property: Orleans.Id(3)] Guid CommandId,
    [property: Orleans.Id(4)] ResourceLifetimeV1 Lifetime,
    [property: Orleans.Id(5)] PhysicalShardRecord PhysicalOwner,
    [property: Orleans.Id(6)] long AppliedPosition,
    [property: Orleans.Id(7)] string IntentDigest);
