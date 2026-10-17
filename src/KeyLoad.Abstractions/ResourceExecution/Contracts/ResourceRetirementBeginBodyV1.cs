namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementBeginBodyAlias)]
internal sealed record ResourceRetirementBeginBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementRequestV1 Request)
{
    [Orleans.Id(1)]
    public ResourceRetirementPreparationControlV1? Preparation
    {
        get; init;
    }

    [Orleans.Id(2)]
    public System.Collections.Immutable.ImmutableArray<ResourceRetirementOwnerStatusV1> OwnerStatuses
    {
        get; init;
    }
}
