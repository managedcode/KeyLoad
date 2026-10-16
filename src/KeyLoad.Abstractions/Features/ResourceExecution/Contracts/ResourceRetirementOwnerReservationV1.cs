using System.Collections.Immutable;

namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceRetirementContract.ResourceRetirementOwnerReservationAlias)]
internal sealed record ResourceRetirementOwnerReservationV1(
    [property: Orleans.Id(0)] ResourceRetirementPreparationControlV1 Control,
    [property: Orleans.Id(1)] RegisteredPhysicalOwnerV1 Receiver,
    [property: Orleans.Id(2)] ImmutableArray<ResourceRetirementAcceptedMoveV1> LocalAcceptedMoves,
    [property: Orleans.Id(3)] ResourceRetirementPhaseReceiptV1 Receipt)
{
    [Orleans.Id(4)]
    public ResourceRetirementAttemptV1? Attempt
    {
        get; init;
    }
    [Orleans.Id(5)]
    public string? OriginalFingerprint
    {
        get; init;
    }
}
