namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed record ScaleServerHardware(string Kernel, string Architecture, string CpuVendor,
    int CpuFamily, int CpuModel, int CpuStepping, int LogicalCpuCount, int PhysicalCoreCount,
    string LogicalCpuMembership, string[] PhysicalCoreMembership, long MemoryBytes);

internal sealed record ScaleServerEnvelope(string CpuQuota, string CpuSet, string MemoryLimitBytes);

internal sealed record ScaleServerContainer(string Resource, string ContainerId, string ImageId, string StartedAt,
    string State, string CpuLimit, string MemoryLimitBytes, string EffectiveCpuQuota, string EffectiveCpuSet,
    string EffectiveMemoryLimitBytes, long ObservedCpuUsageUsec,
    long MaxObservedCgroupMemoryBytes, long MaxObservedRssBytes, int SampleCount, ScaleServerMount[] WritableMounts);

internal sealed record ScaleServerMount(string ContainerPath, string MountType, string FileSystem, long CapacityBytes, long AvailableBytes);
