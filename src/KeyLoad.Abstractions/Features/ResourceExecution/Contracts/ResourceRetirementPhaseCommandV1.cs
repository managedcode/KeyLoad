namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementPhaseCommandAlias)]
internal sealed record ResourceRetirementPhaseCommandV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid RetirementId,
    [property: Orleans.Id(2)] ResourceRetirementPhase Phase,
    [property: Orleans.Id(3)] ReadOnlyMemory<byte> Body);
