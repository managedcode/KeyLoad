namespace KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;

internal sealed record ScaleServerMount(string ContainerPath, string MountType, string FileSystem, long CapacityBytes, long AvailableBytes);
