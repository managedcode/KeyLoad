using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record SampleChunkSchedulingRf3Prepared(SampleChunkJobRevocationScenario Scenario,
    IReadOnlyList<ReplicaSiloDiscovery> Discovery, Guid Arm, Guid MergeArm,
    RequestCqrsProbeMarkerRecord ReturnedMarker, RequestCqrsProbeMarkerRecord MergeMarker,
    SampleChunkNativeJobRf3Receipt Job);
