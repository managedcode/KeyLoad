namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed record ScaleServerHardware(string Kernel, string Architecture, string CpuVendor,
    int CpuFamily, int CpuModel, int CpuStepping, int LogicalCpuCount, int PhysicalCoreCount,
    string LogicalCpuMembership, string[] PhysicalCoreMembership, long MemoryBytes);
