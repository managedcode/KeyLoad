using System.Text;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class ScaleServerCgroupHierarchy
{
    private const string MountInfoSeparator = " - ";

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
        var mounts = await BoundedText.ReadAsync(MountInfoPath, budget.Settings.MaxFileBytes, token, budget);
        var controllers = await BoundedText.ReadAsync(Path.Combine(root, ControllerFile),
            budget.Settings.MaxFileBytes, token, budget);
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
        const int CountInitialValue = 0;
        const char LineFeedCharacter = '\n';
        const int BoundaryValue = 0;
        const char SpaceCharacter = ' ';
        const int ElementIndex = 3;
        const int IsRootMountBoundaryValue = 5;
        const int FirstIndex = 0;
        const int IsRootMountElementIndex = 4;
        const int SingleRootMountCount = 1;

        var count = CountInitialValue;
        foreach (var line in mountInfo.Split(LineFeedCharacter))
        {
            var separator = line.IndexOf(MountInfoSeparator, StringComparison.Ordinal);
            if (separator < BoundaryValue)
            {
                continue;
            }

            var before = line[..separator].Split(SpaceCharacter, StringSplitOptions.RemoveEmptyEntries);
            var after = line[(separator + ElementIndex)..].Split(SpaceCharacter, StringSplitOptions.RemoveEmptyEntries);
            if (before.Length < IsRootMountBoundaryValue || after.Length < 1 || after[FirstIndex] != CgroupV2)
            {
                continue;
            }

            var mountRoot = DecodePath(before[ElementIndex]);
            var mountPoint = DecodePath(before[IsRootMountElementIndex]);
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
        return count == SingleRootMountCount;
    }

    private static string? DecodePath(string value)
    {
        const int IndexInitialValue = 0;
        const char BackslashCharacter = '\\';
        const int Step = 3;
        const int DecodePathStep = 1;
        const int LengthValue = 3;
        const int StructuralValue = 3;

        var decoded = new StringBuilder(value.Length);
        for (var index = IndexInitialValue; index < value.Length; index++)
        {
            if (value[index] != BackslashCharacter)
            {
                decoded.Append(value[index]);
                continue;
            }
            if (index + Step >= value.Length || !TryOctal(value.AsSpan(index + DecodePathStep, LengthValue), out var code))
            {
                return null;
            }

            decoded.Append((char)code);
            index += StructuralValue;
        }
        return decoded.ToString();
    }

    private static bool TryOctal(ReadOnlySpan<char> digits, out int value)
    {
        const int StructuralValue = 0;
        const char CharacterToken = '0';
        const char TryOctalCharacterToken = '7';
        const int ScaleFactor = 8;

        value = StructuralValue;
        foreach (var digit in digits)
        {
            if (digit is < CharacterToken or > TryOctalCharacterToken)
            {
                return false;
            }

            value = checked(value * ScaleFactor + digit - CharacterToken);
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
