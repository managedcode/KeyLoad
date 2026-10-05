using System.Text;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class ScaleServerCgroupHierarchy
{
    private const string MountInfoPath = "/proc/self/mountinfo";
    private const string ControllerFile = "cgroup.controllers";
    private const string CpuController = "cpu";
    private const string MemoryController = "memory";
    private const string CpusetController = "cpuset";
    private const string RootMount = "/";
    private const string CgroupV2 = "cgroup2";

    internal static async Task<bool> IsSupportedRootAsync(string root, ScaleServerResourceSampleBudget budget,
        CancellationToken token)
    {
        var mounts = await BoundedText.ReadAsync(MountInfoPath, ScaleServerResourceBounds.MaxFileBytes, token, budget);
        var controllers = await BoundedText.ReadAsync(Path.Combine(root, ControllerFile),
            ScaleServerResourceBounds.MaxFileBytes, token, budget);
        return mounts is not null && controllers is not null && IsRootMount(mounts, root)
            && HasRequiredControllers(controllers);
    }

    internal static string? ReadRootLimit(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return null;
        }
        catch (FileNotFoundException) { return "max"; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static bool IsRootMount(string mountInfo, string root)
    {
        var count = 0;
        foreach (var line in mountInfo.Split('\n'))
        {
            var separator = line.IndexOf(" - ", StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var before = line[..separator].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var after = line[(separator + 3)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (before.Length < 5 || after.Length < 1 || after[0] != CgroupV2)
            {
                continue;
            }

            var mountRoot = DecodePath(before[3]);
            var mountPoint = DecodePath(before[4]);
            if (mountRoot is null || mountPoint is null)
            {
                return false;
            }

            if (mountPoint != root)
            {
                continue;
            }

            if (mountRoot != RootMount)
            {
                return false;
            }

            count++;
        }
        return count == 1;
    }

    private static string? DecodePath(string value)
    {
        var decoded = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '\\')
            {
                decoded.Append(value[index]);
                continue;
            }
            if (index + 3 >= value.Length || !TryOctal(value.AsSpan(index + 1, 3), out var code))
            {
                return null;
            }

            decoded.Append((char)code);
            index += 3;
        }
        return decoded.ToString();
    }

    private static bool TryOctal(ReadOnlySpan<char> digits, out int value)
    {
        value = 0;
        foreach (var digit in digits)
        {
            if (digit is < '0' or > '7')
            {
                return false;
            }

            value = checked(value * 8 + digit - '0');
        }
        return true;
    }

    private static bool HasRequiredControllers(string value)
    {
        var controllers = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return controllers.Contains(CpuController, StringComparer.Ordinal)
            && controllers.Contains(MemoryController, StringComparer.Ordinal)
            && controllers.Contains(CpusetController, StringComparer.Ordinal);
    }
}
