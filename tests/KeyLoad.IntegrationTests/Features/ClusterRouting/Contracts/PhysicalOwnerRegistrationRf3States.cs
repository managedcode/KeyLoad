namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal enum PhysicalOwnerRegistrationRf3Phase
{
    WaveStart,
    LogService,
    LogWatch,
    HealthSend,
    Fingerprint,
    DirectoryRead,
    SourcePublicClosed,
    DestinationPublicClosed,
    StopForReopen,
    Reopen
}

internal enum PhysicalOwnerRegistrationRf3ResourceState
{
    NotObserved,
    Running,
    FailedToStart,
    Exited,
    Finished,
    Other
}
