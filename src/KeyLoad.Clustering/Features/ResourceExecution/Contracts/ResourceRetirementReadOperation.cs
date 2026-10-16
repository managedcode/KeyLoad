namespace KeyLoad.Clustering;

[global::Orleans.Alias(ResourceRetirementReadContract.OperationAlias)]
internal enum ResourceRetirementReadOperation
{
    State = 1,
    Fence = 2,
    CaptureRetiringInventory = 3,
    Attempt = 4,
    PreparedOperation = 5,
    Preparation = 6,
    OwnerStatus = 7,
    IssueOwnerReservation = 9,
    ActualOwnerWorkJoin = 10,
    DropParent = 11,
    OriginalWorkSeal = 12,
    OriginalD3Seal = 13,
    IssueSourceSeal = 14,
    CleanupProgress = 15,
    OwnerRelease = 16,
    IssueCleanupPage = 17
}
