namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Closed test-owned stages; no database authority or caller payload.</summary>
internal enum FollowerDocumentRf3FailureStage
{
    StartingWave, CapturingHeldRead, ChangingAndReleasing, ObservingOriginalAndContinuing,
    KillingOtherVoter, RestartingOtherVoter, WaitingRestoredHealth, ReadingRestoredStatus,
    ReadingRestoredDiscovery, ConnectingRestoredAdministrator, ConnectingRestoredCaller,
    VerifyingRestoredHealthy, ReplayingOriginalReceipt, VerifyingReplayDocument,
    KillingRestoredFollower, RestartingRestoredFollower, VerifyingColdFollower
}
