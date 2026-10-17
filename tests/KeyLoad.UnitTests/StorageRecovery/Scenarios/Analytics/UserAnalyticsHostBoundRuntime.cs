using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record UserAnalyticsHostBoundRuntime(UserAnalyticsRuntime Runtime, UserAnalyticsHostBoundWork Work);
