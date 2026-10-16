namespace KeyLoad;

[Orleans.Alias(ResourceRetirementContract.ResourceRetirementAttemptPurposeAlias)]
internal enum ResourceRetirementAttemptPurpose : int
{
    SealBarrier = 1,
    OwnerReservation = 2,
    CleanupPage = 3
}
