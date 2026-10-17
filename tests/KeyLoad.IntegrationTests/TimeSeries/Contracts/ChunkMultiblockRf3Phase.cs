namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal enum ChunkMultiblockRf3Phase
{
    Opened,
    OriginalPending,
    OriginalStable,
    FirstExpired,
    SecondExpired,
    HealthyPending,
    HealthyStable
}
