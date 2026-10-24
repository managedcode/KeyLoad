namespace KeyLoad.IntegrationTests.TimeSeries;

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
