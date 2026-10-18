namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal enum ChunkMultiblockClusterPhase
{
    Opened,
    OriginalPending,
    OriginalStable,
    FirstExpired,
    SecondExpired,
    HealthyPending,
    HealthyStable
}
