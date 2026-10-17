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
