namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record MigratedCatalogScopeState(CommandRequest Refused,
    AtomicPartitionPlacementResolution Fallback);
