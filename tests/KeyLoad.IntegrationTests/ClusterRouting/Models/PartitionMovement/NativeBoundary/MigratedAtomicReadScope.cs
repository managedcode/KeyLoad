namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record MigratedAtomicReadScope(EntityRef Reference,
    ErrorCode CatalogCode, string CatalogDetail);
