namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceRetirementContract.ResourceRetirementRecordReservationBodyAlias)]
internal sealed record ResourceRetirementRecordReservationBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementPreparationControlV1 Control,
    [property: Orleans.Id(1)] ResourceRetirementOwnerReservationV1 Reservation);
