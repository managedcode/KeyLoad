using KeyLoad.Clustering;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record ConnectionQueryContextColdResult(RequestCqrsRf3Callers Administrator, ReplicaSiloDiscovery[] Discovery);
