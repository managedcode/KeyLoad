namespace KeyLoad.IntegrationTests.Features.Search;

internal enum NativeTextMaintenanceFlowPhase
{
    Connection, Seed, Request, DeniedOwner, Build, OriginalCanonical, OriginalSelected,
    Mutation, StaleSelected, OriginalPrefix, Restore, ChangedCanonical, ChangedSelected,
    MutationReplay, RestoreReplay, FinalCanonical, Release, FinalEmptyUkrainian, FinalEmptyEnglish, ColdContinuation
}
