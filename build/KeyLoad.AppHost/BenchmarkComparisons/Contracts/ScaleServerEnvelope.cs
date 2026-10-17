namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed record ScaleServerEnvelope(string CpuQuota, string CpuSet, string MemoryLimitBytes);
