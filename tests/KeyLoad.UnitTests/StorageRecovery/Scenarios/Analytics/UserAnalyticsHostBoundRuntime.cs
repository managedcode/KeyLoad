using KeyLoad.Server;

namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed record UserAnalyticsHostBoundRuntime(UserAnalyticsRuntime Runtime, UserAnalyticsHostBoundWork Work);
