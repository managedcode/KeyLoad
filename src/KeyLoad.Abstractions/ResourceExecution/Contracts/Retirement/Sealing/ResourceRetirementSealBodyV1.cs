namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementSealBodyAlias)]
internal sealed record ResourceRetirementSealBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementControlV1 Control,
    [property: Orleans.Id(1)] ResourceOwnerInventoryResultV1 Inventory)
{
    [Orleans.Id(2)]
    public ResourceRetirementAttemptV1? Attempt
    {
        get; init;
    }
}
