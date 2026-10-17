namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal enum PhysicalOwnerRegistrationRf3ResourceState
{
    NotObserved,
    Running,
    FailedToStart,
    Exited,
    Finished,
    Other
}
