namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record RequestCqrsProbeSnapshot(
    IReadOnlyList<RequestCqrsProbeLoadedArm> Arms,
    IReadOnlyList<RequestCqrsProbeReleaseRecord> Releases,
    IReadOnlyList<RequestCqrsProbeMarkerRecord> Markers,
    int FileCount,
    long AggregateBytes);

internal sealed record RequestCqrsProbeLoadedArm(RequestCqrsProbeArmRecord Record, byte[] ExactBytes);

