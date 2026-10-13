namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record OwnerCapacityOperatingSystemSample(
    long Timestamp, DateTimeOffset SampledAt, long AvailableDiskBytes, long TotalDiskBytes,
    long MemoryCurrentBytes, long MemoryLimitBytes, long CpuUsageMicroseconds,
    long CpuQuotaMicroseconds, long CpuPeriodMicroseconds, string? HostDomain);
