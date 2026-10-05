using System.Globalization;
using KeyLoad.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class ScaleServerCgroupOracle
{
    private const string MembershipPath = "/proc/self/cgroup";
    private const string MountInfoPath = "/proc/self/mountinfo";
    private const string CgroupRoot = "/sys/fs/cgroup";
    private const string ControllerFile = "cgroup.controllers";
    private const string CpuFile = "cpu.max";
    private const string MemoryFile = "memory.max";
    private const string CpuSetFile = "cpuset.cpus.effective";
    private const string Unlimited = "max";
    private const int FileLimit = 4096;
    private const int AncestorLimit = 64;

    internal static ScaleServerEnvelope ReadCurrent()
    {
        if (!HasActualRoot())
        {
            throw new InvalidDataException("Native cgroup hierarchy root was not verified.");
        }

        var row = ReadBounded(MembershipPath).Split('\n')
            .Single(line => line.StartsWith("0::", StringComparison.Ordinal));
        if (!row.StartsWith("0::/", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Native unified cgroup membership was malformed.");
        }

        var directory = Path.GetFullPath(Path.Combine(CgroupRoot, row[3..].TrimStart('/')));
        var root = Path.GetFullPath(CgroupRoot);
        if (directory != root && !directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Native cgroup membership escaped its verified root.");
        }

        var cpu = decimal.MaxValue;
        var memory = long.MaxValue;
        string? cpuSet = null;
        for (var depth = 0; depth < AncestorLimit; depth++)
        {
            var isRoot = directory == root;
            var cpuText = isRoot ? ReadRootLimit(Path.Combine(directory, CpuFile)) : ReadBounded(Path.Combine(directory, CpuFile));
            var cpuFields = cpuText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!(isRoot && cpuFields.Length == 1 && cpuFields[0] == Unlimited))
            {
                if (cpuFields.Length != 2 || !long.TryParse(cpuFields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var period)
                    || period <= 0)
                {
                    throw new InvalidDataException("Native cgroup CPU limit was malformed.");
                }

                if (cpuFields[0] != Unlimited)
                {
                    if (!long.TryParse(cpuFields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var quota) || quota <= 0)
                    {
                        throw new InvalidDataException("Native cgroup CPU quota was malformed.");
                    }

                    cpu = Math.Min(cpu, (decimal)quota / period);
                }
            }
            var memoryText = (isRoot ? ReadRootLimit(Path.Combine(directory, MemoryFile)) : ReadBounded(Path.Combine(directory, MemoryFile))).Trim();
            if (memoryText != Unlimited)
            {
                if (!long.TryParse(memoryText, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) || limit <= 0)
                {
                    throw new InvalidDataException("Native cgroup memory limit was malformed.");
                }

                memory = Math.Min(memory, limit);
            }
            cpuSet ??= ReadBounded(Path.Combine(directory, CpuSetFile)).Trim();
            if (directory == root)
            {
                break;
            }

            directory = Path.GetDirectoryName(directory) ?? root;
        }
        if (directory != root || string.IsNullOrWhiteSpace(cpuSet))
        {
            throw new InvalidDataException("Native cgroup ancestry was incomplete.");
        }

        return new(cpu == decimal.MaxValue ? Unlimited : cpu.ToString(CultureInfo.InvariantCulture), cpuSet,
            memory == long.MaxValue ? Unlimited : memory.ToString(CultureInfo.InvariantCulture));
    }

    private static bool HasActualRoot()
    {
        var matches = 0;
        foreach (var line in ReadBounded(MountInfoPath).Split('\n'))
        {
            var separator = line.IndexOf(" - ", StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var before = line[..separator].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var after = line[(separator + 3)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (before.Length < 5 || after.Length == 0 || after[0] != "cgroup2")
            {
                continue;
            }

            var mountRoot = DecodePath(before[3]);
            var mountPoint = DecodePath(before[4]);
            if (mountPoint != CgroupRoot)
            {
                continue;
            }

            if (mountRoot != "/")
            {
                return false;
            }

            matches++;
        }
        var controllers = ReadBounded(Path.Combine(CgroupRoot, ControllerFile))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return matches == 1 && controllers.Contains("cpu", StringComparer.Ordinal)
            && controllers.Contains("memory", StringComparer.Ordinal)
            && controllers.Contains("cpuset", StringComparer.Ordinal);
    }

    private static string? DecodePath(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '\\')
            { result.Append(value[index]); continue; }
            if (index + 3 >= value.Length)
            {
                return null;
            }

            var digits = value.AsSpan(index + 1, 3);
            var code = 0;
            foreach (var digit in digits)
            {
                if (digit is < '0' or > '7')
                {
                    return null;
                }

                code = code * 8 + digit - '0';
            }
            result.Append((char)code);
            index += 3;
        }
        return result.ToString();
    }

    private static string ReadRootLimit(string path)
    {
        try
        { using (File.OpenRead(path)) { } }
        catch (FileNotFoundException) { return Unlimited; }
        throw new InvalidDataException("Kernel cgroup root unexpectedly exposed a child limit file.");
    }

    private static string ReadBounded(string path)
    {
        using var stream = File.OpenRead(path);
        var bytes = new byte[FileLimit + 1];
        var total = 0;
        while (total < bytes.Length)
        {
            var count = stream.Read(bytes, total, bytes.Length - total);
            if (count == 0)
            {
                break;
            }

            total += count;
        }
        if (total > FileLimit || stream.ReadByte() != -1)
        {
            throw new InvalidDataException("Native cgroup evidence exceeded its bound.");
        }

        return System.Text.Encoding.UTF8.GetString(bytes, 0, total);
    }
}
