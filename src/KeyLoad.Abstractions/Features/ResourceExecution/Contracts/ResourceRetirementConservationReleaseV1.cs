namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementConservationReleaseAlias)]
internal sealed record ResourceRetirementConservationReleaseV1(
    [property: Orleans.Id(0)] ResourceRetirementOwnerReleaseV1 OwnerRelease,
    [property: Orleans.Id(1)] ResourceRetirementConservationFloorV1 Before,
    [property: Orleans.Id(2)] ResourceRetirementConservationFloorV1 After);
