using System.Globalization;
using System.Runtime.InteropServices;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class ScaleServerHostEvidence
{
    private const string CpuInfo = "/proc/cpuinfo";
    private const string MemoryInfo = "/proc/meminfo";
    private const string CpuOnline = "/sys/devices/system/cpu/online";
    private const string CpuMaximum = "cpu.max";
    private const string MemoryMaximum = "memory.max";
    private const string KernelExecutable = "uname";
    private const string Unlimited = "max";
    private const string VendorField = "vendor_id";
    private const string FamilyField = "cpu family";
    private const string ModelField = "model";
    private const string SteppingField = "stepping";
    private const string ProcessorField = "processor";
    private const string PhysicalIdField = "physical id";
    private const string CoreIdField = "core id";
    private const string MemTotalField = "MemTotal";
    private const int Kilobytes = 1024;

    internal static async Task<(ScaleServerHardware? Hardware, ScaleServerEnvelope? Envelope)> ReadAsync(
        CancellationToken token)
    {
        if (!OperatingSystem.IsLinux()) return (null, null);
        var budget = new ScaleServerResourceSampleBudget(ScaleServerResourceBounds.MaxSampleMetadataBytes);
        var cpu = await BoundedText.ReadAsync(CpuInfo, ScaleServerResourceBounds.MaxHardwareBytes, token, budget);
        var memory = await BoundedText.ReadAsync(MemoryInfo, ScaleServerResourceBounds.MaxFileBytes, token, budget);
        var online = await BoundedText.ReadAsync(CpuOnline, ScaleServerResourceBounds.MaxFileBytes, token, budget);
        if (cpu is null || memory is null || online is null) return (null, null);
        var values = ParseCpu(cpu);
        if (values is null || !long.TryParse(ReadValue(memory, MemTotalField), NumberStyles.None,
                CultureInfo.InvariantCulture, out var memoryKb)) return (null, null);
        var kernel = await ScaleServerResourceProcess.RunAsync(KernelExecutable, ["-r"], token, budget);
        if (string.IsNullOrWhiteSpace(kernel)) return (null, null);
        var logical = CountOnline(online);
        var cores = CoreMembership(cpu);
        var envelope = await ReadEnvelopeAsync(token, budget);
        if (logical < 1 || cores.Physical.Length < 1 || cores.Logical.Split(',').Length != logical) return (null, envelope);
        var hardware = new ScaleServerHardware(kernel.Trim(), RuntimeInformation.OSArchitecture.ToString(), values.Value.Vendor,
            values.Value.Family, values.Value.Model, values.Value.Stepping, logical, cores.Physical.Length,
            cores.Logical, cores.Physical, checked(memoryKb * Kilobytes));
        return (hardware, envelope);
    }

    private static (string Vendor, int Family, int Model, int Stepping)? ParseCpu(string text)
    {
        string? vendor = null;
        string? family = null;
        string? model = null;
        string? stepping = null;
        foreach (var line in text.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator < 1) continue;
            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (value is null) continue;
            var accepted = name switch
            {
                VendorField => SetConsistent(ref vendor, value),
                FamilyField => SetConsistent(ref family, value),
                ModelField => SetConsistent(ref model, value),
                SteppingField => SetConsistent(ref stepping, value),
                _ => true
            };
            if (!accepted) return null;
        }
        return vendor is null || !int.TryParse(family, NumberStyles.None, CultureInfo.InvariantCulture, out var familyValue)
            || !int.TryParse(model, NumberStyles.None, CultureInfo.InvariantCulture, out var modelValue)
            || !int.TryParse(stepping, NumberStyles.None, CultureInfo.InvariantCulture, out var steppingValue)
            ? null : (vendor, familyValue, modelValue, steppingValue);
    }

    private static bool SetConsistent(ref string? current, string value)
    {
        if (current is not null) return string.Equals(current, value, StringComparison.Ordinal);
        current = value;
        return true;
    }

    private static string? ReadValue(string text, string name)
    {
        foreach (var line in text.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator > 0 && line[..separator].Trim().Equals(name, StringComparison.Ordinal))
                return line[(separator + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }
        return null;
    }

    private static int CountOnline(string text)
    {
        var total = 0;
        foreach (var range in text.Trim().Split(','))
        {
            var ends = range.Split('-');
            if (!int.TryParse(ends[0], out var first)) return 0;
            var last = first;
            if (ends.Length == 2 && !int.TryParse(ends[1], out last)) return 0;
            total = checked(total + last - first + 1);
        }
        return total;
    }

    private static (string Logical, string[] Physical) CoreMembership(string cpu)
    {
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        var logical = new HashSet<string>(StringComparer.Ordinal);
        string? physical = null;
        string? core = null;
        string? processor = null;
        foreach (var line in cpu.Split('\n').Append(string.Empty))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (physical is not null && core is not null) pairs.Add(physical + ":" + core);
                if (processor is not null) logical.Add(processor);
                physical = null; core = null; processor = null;
                continue;
            }
            var separator = line.IndexOf(':');
            if (separator < 1) continue;
            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (name == PhysicalIdField) physical = value;
            if (name == CoreIdField) core = value;
            if (name == ProcessorField) processor = value;
        }
        return pairs.Count == 0 ? (string.Empty, []) :
            (string.Join(',', logical.Order(StringComparer.Ordinal)), pairs.Order(StringComparer.Ordinal).ToArray());
    }

    internal static async Task<ScaleServerEnvelope?> ReadCgroupEnvelopeAsync(string cgroupPath,
        CancellationToken token, ScaleServerResourceSampleBudget budget)
    {
        if (!OperatingSystem.IsLinux() || cgroupPath.Length == 0 || cgroupPath[0] != '/') return null;
        var root = Path.GetFullPath("/sys/fs/cgroup");
        var current = Path.GetFullPath(Path.Combine(root, cgroupPath.TrimStart('/')));
        if (current != root && !current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
        return await ReadEnvelopeFromDirectoryAsync(current, root, token, budget);
    }

    private static async Task<ScaleServerEnvelope?> ReadEnvelopeAsync(CancellationToken token,
        ScaleServerResourceSampleBudget budget)
    {
        var membership = await BoundedText.ReadAsync("/proc/self/cgroup", ScaleServerResourceBounds.MaxFileBytes, token, budget);
        var row = membership?.Split('\n').FirstOrDefault(line => line.StartsWith("0::", StringComparison.Ordinal));
        if (row is null) return null;
        if (!row.StartsWith("0::/", StringComparison.Ordinal)) return null;
        var current = Path.GetFullPath(Path.Combine("/sys/fs/cgroup", row[3..].TrimStart('/')));
        var root = Path.GetFullPath("/sys/fs/cgroup");
        if (current != root && !current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
        return await ReadEnvelopeFromDirectoryAsync(current, root, token, budget);
    }

    private static async Task<ScaleServerEnvelope?> ReadEnvelopeFromDirectoryAsync(string current, string root,
        CancellationToken token, ScaleServerResourceSampleBudget budget)
    {
        if (!await ScaleServerCgroupHierarchy.IsSupportedRootAsync(root, token, budget)) return null;
        decimal? cpu = null;
        long? memory = null;
        string? cpuSet = null;
        var directory = current;
        for (var depth = 0; depth < ScaleServerResourceBounds.MaxCgroupAncestors; depth++)
        {
            var isRoot = directory == root;
            var cpuPath = Path.Combine(directory, CpuMaximum);
            var memoryPath = Path.Combine(directory, MemoryMaximum);
            var cpuText = isRoot
                ? ScaleServerCgroupHierarchy.ReadRootLimit(cpuPath)
                : await BoundedText.ReadAsync(cpuPath, ScaleServerResourceBounds.MaxFileBytes, token, budget);
            var memoryText = isRoot
                ? ScaleServerCgroupHierarchy.ReadRootLimit(memoryPath)
                : await BoundedText.ReadAsync(memoryPath, ScaleServerResourceBounds.MaxFileBytes, token, budget);
            var setText = await BoundedText.ReadAsync(Path.Combine(directory, "cpuset.cpus.effective"), ScaleServerResourceBounds.MaxFileBytes, token, budget);
            if (cpuText is null || memoryText is null || string.IsNullOrWhiteSpace(setText)) return null;
            if (cpuSet is null) cpuSet = setText.Trim();
            var parts = cpuText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!(isRoot && parts.Length == 1 && parts[0] == Unlimited))
            {
                if (parts.Length != 2 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var period)
                    || period <= 0) return null;
                if (parts[0] != Unlimited)
                {
                    if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var quota)
                        || quota <= 0) return null;
                    var cores = (decimal)quota / period;
                    cpu = cpu is null ? cores : Math.Min(cpu.Value, cores);
                }
            }
            var limitText = memoryText.Trim();
            if (limitText != Unlimited)
            {
                if (!long.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) || limit <= 0) return null;
                memory = memory is null ? limit : Math.Min(memory.Value, limit);
            }
            if (directory == root) break;
            directory = Path.GetDirectoryName(directory) ?? root;
        }
        if (directory != root) return null;
        var cpuValue = cpu?.ToString(CultureInfo.InvariantCulture) ?? Unlimited;
        var memoryValue = memory?.ToString(CultureInfo.InvariantCulture) ?? Unlimited;
        return cpuSet is null ? null : new ScaleServerEnvelope(cpuValue, cpuSet, memoryValue);
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
            if (limit < 1 || !stream.CanSeek || stream.Length > limit) return null;
            var buffer = new byte[limit];
            var total = 0;
            while (true)
            {
                if (total == limit)
                {
                    if (stream.Length != total) return null;
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
                if (total > limit) return null;
            }
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
