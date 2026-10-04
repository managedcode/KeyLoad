namespace KeyLoad.Orleans.Features.ResourceExecution;

internal enum CacheVoterSlot : byte
{
    Slot0 = 0,
    Slot1 = 1,
    Slot2 = 2
}

internal enum CachePhysicalRole : byte
{
    Canonical = 1
}

internal enum CacheControlOperation : byte
{
    Prepare = 1,
    Grant = 2,
    Revoke = 3,
    Refresh = 4
}

internal enum CacheRevokeEffect : byte
{
    None = 0,
    PendingRemoved = 1,
    LeaseWithdrawn = 2,
    Both = 3
}

internal enum CacheControlStatus : byte
{
    Ready = 1,
    AcceptedActive = 2,
    AcceptedCold = 3,
    Busy = 4,
    NotReady = 5,
    PolicyMismatch = 6,
    StaleChallenge = 7,
    Replay = 8,
    Rejected = 9,
    Closed = 10,
    Capacity = 11,
    Revoked = 12,
    HintAcknowledged = 13
}
