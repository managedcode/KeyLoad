namespace KeyLoad.Core.Features.BlobStorage;

internal enum BlobRestorePhase
{
    States,
    Heads,
    Quotas,
    VerifyStates,
    VerifyHeads,
    VerifyResources,
    Complete
}
