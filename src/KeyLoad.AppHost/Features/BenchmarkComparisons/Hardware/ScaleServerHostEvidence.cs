using System.Globalization;
using System.Runtime.InteropServices;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class ScaleServerHostEvidence
{
    private const string UnifiedMembershipPrefix = "0::";
    private const string UnifiedAbsoluteMembershipPrefix = "0::/";

    private const string CpuInfo = "/proc/cpuinfo";
    private const string MemoryInfo = "/proc/meminfo";
    private const string CpuOnline = "/sys/devices/system/cpu/online";
    private const string KernelExecutable = "uname";
    private const string MemTotalField = "MemTotal";
    private const int Kilobytes = 1024;

    internal static async Task<(ScaleServerHardware? Hardware, ScaleServerEnvelope? Envelope)> ReadAsync(
        CancellationToken token)
    {
        if (!OperatingSystem.IsLinux())
        {
            return (null, null);
        }

        var budget = new ScaleServerResourceSampleBudget(ScaleServerResourceBounds.MaxSampleMetadataBytes);
        var cpu = await BoundedText.ReadAsync(CpuInfo, ScaleServerResourceBounds.MaxHardwareBytes, token, budget);
        var memory = await BoundedText.ReadAsync(MemoryInfo, ScaleServerResourceBounds.MaxFileBytes, token, budget);
        var online = await BoundedText.ReadAsync(CpuOnline, ScaleServerResourceBounds.MaxFileBytes, token, budget);
        if (cpu is null || memory is null || online is null)
        {
            return (null, null);
        }

        var values = ScaleServerHostCpuParser.ParseCpu(cpu);
        if (values is null || !long.TryParse(ScaleServerHostCpuParser.ReadValue(memory, MemTotalField), NumberStyles.None,
                CultureInfo.InvariantCulture, out var memoryKb))
        {
            return (null, null);
        }

        var kernel = await ScaleServerResourceProcess.RunAsync(KernelExecutable, ["-r"], token, budget);
        if (string.IsNullOrWhiteSpace(kernel))
        {
            return (null, null);
        }

        var logical = ScaleServerHostCpuParser.CountOnline(online);
        var cores = ScaleServerHostCpuParser.CoreMembership(cpu);
        var envelope = await ReadEnvelopeAsync(budget, token);
        if (logical < 1 || cores.Physical.Length < 1 || cores.Logical.Split(',').Length != logical)
        {
            return (null, envelope);
        }

        var hardware = new ScaleServerHardware(kernel.Trim(), RuntimeInformation.OSArchitecture.ToString(), values.Value.Vendor,
            values.Value.Family, values.Value.Model, values.Value.Stepping, logical, cores.Physical.Length,
            cores.Logical, cores.Physical, checked(memoryKb * Kilobytes));
        return (hardware, envelope);
    }

    internal static async Task<ScaleServerEnvelope?> ReadCgroupEnvelopeAsync(string cgroupPath,
        ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux() || cgroupPath.Length == 0 || cgroupPath[0] != '/')
        {
            return null;
        }

        var root = Path.GetFullPath("/sys/fs/cgroup");
        var current = Path.GetFullPath(Path.Combine(root, cgroupPath.TrimStart('/')));
        if (current != root && !current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        return await ScaleServerCgroupEnvelopeReader.ReadFromDirectoryAsync(current, root, budget, token);
    }

    private static async Task<ScaleServerEnvelope?> ReadEnvelopeAsync(ScaleServerResourceSampleBudget budget,
        CancellationToken token)
    {
        var membership = await BoundedText.ReadAsync("/proc/self/cgroup", ScaleServerResourceBounds.MaxFileBytes, token, budget);
        var row = membership?.Split('\n').FirstOrDefault(line => line.StartsWith(UnifiedMembershipPrefix, StringComparison.Ordinal));
        if (row is null)
        {
            return null;
        }

        if (!row.StartsWith(UnifiedAbsoluteMembershipPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var current = Path.GetFullPath(Path.Combine("/sys/fs/cgroup", row[3..].TrimStart('/')));
        var root = Path.GetFullPath("/sys/fs/cgroup");
        if (current != root && !current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        return await ScaleServerCgroupEnvelopeReader.ReadFromDirectoryAsync(current, root, budget, token);
    }
}

internal static class BoundedText
{
    internal static async Task<string?> ReadAsync(string path, int maximum, CancellationToken token,
        ScaleServerResourceSampleBudget? budget = null)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var limit = budget is null ? maximum : Math.Min(maximum, budget.Remaining);
            if (limit < 1 || !stream.CanSeek || stream.Length > limit)
            {
                return null;
            }

            var buffer = new byte[limit];
            var total = 0;
            while (true)
            {
                if (total == limit)
                {
                    if (stream.Length != total)
                    {
                        return null;
                    }

                    budget?.Charge(total);
                    return System.Text.Encoding.UTF8.GetString(buffer, 0, total);
                }
                var count = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), token);
                if (count == 0)
                {
                    budget?.Charge(total);
                    return System.Text.Encoding.UTF8.GetString(buffer, 0, total);
                }
                total += count;
                if (total > limit)
                {
                    return null;
                }
            }
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
