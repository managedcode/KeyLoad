namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementWorkJoinEvidenceAlias)]
internal sealed record ResourceRetirementWorkJoinEvidenceV1(
    [property: Orleans.Id(0)] ResourceRetirementWorkJoinWitnessV1 Witness,
    [property: Orleans.Id(1)] ReadOnlyMemory<byte> SignedNativeRequest,
    [property: Orleans.Id(2)] ReadOnlyMemory<byte> SignedNativeReply);
