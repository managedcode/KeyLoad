using Microsoft.Extensions.Options;
namespace KeyLoad.Server.Features.ClusterRouting;

internal static class OwnerCapacityCgroupReader
{
    private const string HostIdentityFormat = "N";
    private const string MemoryCurrent = "/sys/fs/cgroup/memory.current";
    private const string MemoryMaximum = "/sys/fs/cgroup/memory.max";
    private const string CpuMaximum = "/sys/fs/cgroup/cpu.max";
    private const string CpuStatistics = "/sys/fs/cgroup/cpu.stat";
    private const string PhysicalHostUuid = "/sys/class/dmi/id/product_uuid";
    private const long NoBytes = 0;

    internal static async Task<OwnerCapacityOperatingSystemSample?> CaptureAsync(string ownerDirectory,
        TimeProvider clock, IOptions<TenantPlacementExecutionOptions> options, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux())
        { return null; }
        var sampledAt = clock.GetUtcNow();
        try
        {
            var memory = await OwnerCapacityNativeFile.ReadAsync(MemoryCurrent, options, token).ConfigureAwait(false);
            var maximum = await OwnerCapacityNativeFile.ReadAsync(MemoryMaximum, options, token).ConfigureAwait(false);
            var cpu = await OwnerCapacityNativeFile.ReadAsync(CpuMaximum, options, token).ConfigureAwait(false);
            var usage = await OwnerCapacityNativeFile.ReadAsync(CpuStatistics, options, token).ConfigureAwait(false);
            if (memory is null || maximum is null || cpu is null || usage is null
                || !OwnerCapacityCgroupValues.Memory(memory, maximum, out var used, out var limit)
                || !OwnerCapacityCgroupValues.CpuLimit(cpu, out var quota, out var period)
                || !OwnerCapacityCgroupValues.CpuUsage(usage, out var consumed))
            { return null; }
            var cpuTimestamp = clock.GetTimestamp();
            var disk = new DriveInfo(ownerDirectory);
            var total = disk.TotalSize;
            var available = disk.AvailableFreeSpace;
            if (!disk.IsReady || total <= NoBytes || available < NoBytes || available > total)
            { return null; }
            var domain = await HostDomainAsync(options, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return new(cpuTimestamp, sampledAt, available, total, used, limit,
                consumed, quota, period, domain);
        }
        catch (IOException)
        { return null; }
        catch (UnauthorizedAccessException)
        { return null; }
    }

    private static async Task<string?> HostDomainAsync(IOptions<TenantPlacementExecutionOptions> options, CancellationToken token)
    {
        try
        {
            var text = await OwnerCapacityNativeFile.ReadAsync(PhysicalHostUuid, options, token).ConfigureAwait(false);
            return Guid.TryParse(text?.Trim(), out var native) && native != Guid.Empty
                ? native.ToString(HostIdentityFormat) : null;
        }
        catch (IOException)
        { return null; }
        catch (UnauthorizedAccessException)
        { return null; }
    }
}
