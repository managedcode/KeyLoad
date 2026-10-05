namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed record ScaleServerResourceEvidence(
    string Schema, string SourceRevision, string WorkflowRunId, string RunAttempt, string JobId,
    string Target, int NodeCount, string Scenario, string Profile, string WorkerSha256,
    ScaleServerHardware? Hardware, ScaleServerEnvelope? AppHostEnvelope, ScaleServerContainer[] Containers,
    string[] MissingEvidence, bool Qualified);

internal sealed record ScaleServerHardware(string Kernel, string Architecture, string CpuVendor,
    int CpuFamily, int CpuModel, int CpuStepping, int LogicalCpuCount, int PhysicalCoreCount,
    string LogicalCpuMembership, string[] PhysicalCoreMembership, long MemoryBytes);

internal sealed record ScaleServerEnvelope(string CpuQuota, string CpuSet, string MemoryLimitBytes);

internal sealed record ScaleServerContainer(string Resource, string ContainerId, string ImageId, string StartedAt,
    string State, string CpuLimit, string MemoryLimitBytes, string EffectiveCpuQuota, string EffectiveCpuSet,
    string EffectiveMemoryLimitBytes, long ObservedCpuUsageUsec,
    long MaxObservedCgroupMemoryBytes, long MaxObservedRssBytes, int SampleCount, ScaleServerMount[] WritableMounts);

internal sealed record ScaleServerMount(string ContainerPath, string MountType, string FileSystem, long CapacityBytes, long AvailableBytes);
