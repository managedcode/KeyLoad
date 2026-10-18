namespace KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;

internal sealed record ScaleServerContainer(string Resource, string ContainerId, string ImageId, string StartedAt,
    string State, string CpuLimit, string MemoryLimitBytes, string EffectiveCpuQuota, string EffectiveCpuSet,
    string EffectiveMemoryLimitBytes, long ObservedCpuUsageUsec,
    long MaxObservedCgroupMemoryBytes, long MaxObservedRssBytes, int SampleCount, ScaleServerMount[] WritableMounts);
