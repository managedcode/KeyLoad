namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record OwnerCapacityMeasuredSample(
    long Sequence, OwnerCapacityOperatingSystemSample OperatingSystem, int CpuBasisPoints);
