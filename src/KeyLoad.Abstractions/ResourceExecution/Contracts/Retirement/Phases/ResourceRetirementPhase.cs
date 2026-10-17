namespace KeyLoad;

[Orleans.Alias(ResourceRetirementContract.ResourceRetirementPhaseAlias)]
internal enum ResourceRetirementPhase : int
{
    Begin = 1,
    FenceOwner = 2,
    RecordOwnerReceipt = 3,
    SealBarrier = 4,
    PrepareAttempt = 5,
    PreparedOperation = 6,
    PrepareRetirement = 7,
    ReserveOwner = 8,
    RecordReservation = 9,
    CommitOwnerWorkJoin = 10,
    RecordOwnerWorkJoin = 11,
    SealAllWork = 12,
    CleanupPage = 20,
    RecordOwnerCleanup = 21,
    FinalizeCatalog = 22,
    ReleaseOwnerTopology = 23,
    RecordOwnerRelease = 24,
    Finish = 25,
    CompleteOwnerCleanup = 26,
    PersistDropParent = 27,
    CompleteDropParent = 28
}
