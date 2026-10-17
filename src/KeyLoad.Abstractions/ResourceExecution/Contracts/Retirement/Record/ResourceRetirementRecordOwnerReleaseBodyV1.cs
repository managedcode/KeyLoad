namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementRecordOwnerReleaseBodyAlias)]
internal sealed record ResourceRetirementRecordOwnerReleaseBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementControlV1 Control,
    [property: Orleans.Id(1)] ResourceRetirementOwnerReleaseV1 OwnerRelease);
