namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementRequestAlias)]
internal sealed record ResourceRetirementRequestV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid RetirementId,
    [property: Orleans.Id(2)] string TenantId,
    [property: Orleans.Id(3)] string DatabaseId,
    [property: Orleans.Id(4)] string ResourceName,
    [property: Orleans.Id(5)] ResourceIncarnationV1 ExpectedIncarnation);
