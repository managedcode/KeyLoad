using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record RequestCqrsRf3ObservedWaveScope(RequestCqrsLifecycleEvidence Lifecycle,
    string? PhysicalShardOverrideNode = null, Guid? PhysicalShardOverrideId = null);
