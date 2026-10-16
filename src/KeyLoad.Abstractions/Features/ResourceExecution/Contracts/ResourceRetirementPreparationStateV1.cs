using System.Collections.Immutable;

namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceRetirementContract.ResourceRetirementPreparationStateAlias)]
internal sealed record ResourceRetirementPreparationStateV1(
    [property: Orleans.Id(0)] ResourceRetirementPreparationControlV1 Control,
    [property: Orleans.Id(1)] ImmutableArray<ResourceRetirementOwnerReservationV1> Reservations,
    [property: Orleans.Id(2)] ResourceRetirementControlV1? BegunControl)
{
    [Orleans.Id(3)]
    public ImmutableArray<ResourceRetirementAttemptV1> OwnerAttempts
    {
        get; init;
    }
}
