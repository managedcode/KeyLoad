namespace KeyLoad.UnitTests.Features.ClientApi;

internal enum NativeCredentialStatusStep
{
    OriginalBoolean = 1,
    OriginalReplay = 2,
    OrdinaryDenied = 3,
    Healthy = 4,
    HealthyReplay = 5,
    OriginalFinalReplay = 6
}
