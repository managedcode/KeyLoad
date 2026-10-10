namespace KeyLoad.Orleans.Features.Search;

internal enum OnlineTextCapabilityKind
{
    ResolveOriginal = 0,
    Capture = 1,
    Seed = 2,
    PreparePage = 3,
    ApplyIntent = 4,
    SettleCheckpoint = 5,
    Validate = 6,
    IssuePublication = 7,
    ReconcileCommitted = 8,
    Abort = 9
}
