namespace KeyLoad.ClusterTests.TimeSeries;

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
