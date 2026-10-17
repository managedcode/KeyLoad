using System.Collections.Immutable;

namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceRetirementContract.ResourceRetirementOwnerStatusAlias)]
internal sealed record ResourceRetirementOwnerStatusV1(
    [property: Orleans.Id(0)] ResourceRetirementOwnerReservationV1 Reservation,
    [property: Orleans.Id(1)] ResourceRetirementOwnerStatusCutV1 Cut,
    [property: Orleans.Id(2)] ImmutableArray<ResourceRetirementAcceptedMoveV1> Remaining)
{
    [Orleans.Id(3)]
    public string? VoterId
    {
        get; init;
    }
    [Orleans.Id(4)]
    public string? FullSiloAddress
    {
        get; init;
    }
}
