namespace KeyLoad.UnitTests.Features.ClientApi;

internal enum NativeSuccessBodyStep
{
    OriginalWrite = 1,
    OriginalReplay = 2,
    RefusedRead = 3,
    HealthyRead = 4,
    Conflict = 5,
    HealthyWrite = 6,
    HealthyReplay = 7,
    FinalRead = 8
}
