using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed record UserAnalyticsHostBoundRuntime(UserAnalyticsRuntime Runtime, UserAnalyticsHostBoundWork Work);
