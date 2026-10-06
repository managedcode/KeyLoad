using System.Globalization;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class ScaleServerCgroupEnvelopeReader
{
    private const char WhitespaceSeparator = ' ';
    private const int PeriodFieldIndex = 1;
    private const int NoLimit = 0;
    private const int CpuLimitFieldCount = 2;

    private const string CpuMaximum = "cpu.max";
    private const string MemoryMaximum = "memory.max";
    private const string Unlimited = "max";

    internal static async Task<ScaleServerEnvelope?> ReadFromDirectoryAsync(string current, string root,
        ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const int DepthInitialValue = 0;

        if (!await ScaleServerCgroupHierarchy.IsSupportedRootAsync(root, budget, token))
        {
            return null;
        }

        decimal? cpu = null;
        long? memory = null;
        string? cpuSet = null;
        var directory = current;
        for (var depth = DepthInitialValue; depth < budget.Settings.MaxCgroupAncestors; depth++)
        {
            var isRoot = directory == root;
            var inputs = await ReadAncestorInputsAsync(directory, isRoot, budget, token);
            if (inputs is null)
            {
                return null;
            }

            if (cpuSet is null)
            {
                cpuSet = inputs.Value.CpuSet.Trim();
            }

            if (!TryAccumulateCpuLimit(inputs.Value.Cpu, isRoot, ref cpu)
                || !TryAccumulateMemoryLimit(inputs.Value.Memory, ref memory))
            {
                return null;
            }

            if (directory == root)
            {
                break;
            }

            directory = Path.GetDirectoryName(directory) ?? root;
        }
        if (directory != root)
        {
            return null;
        }

        var cpuValue = cpu?.ToString(CultureInfo.InvariantCulture) ?? Unlimited;
        var memoryValue = memory?.ToString(CultureInfo.InvariantCulture) ?? Unlimited;
        return cpuSet is null ? null : new ScaleServerEnvelope(cpuValue, cpuSet, memoryValue);
    }

    private static async Task<(string Cpu, string Memory, string CpuSet)?> ReadAncestorInputsAsync(
        string directory, bool isRoot, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const string Path2Text = "cpuset.cpus.effective";

        var cpuPath = Path.Combine(directory, CpuMaximum);
        var memoryPath = Path.Combine(directory, MemoryMaximum);
        var cpuText = isRoot
            ? ScaleServerCgroupHierarchy.ReadRootLimit(cpuPath)
            : await BoundedText.ReadAsync(cpuPath, budget.Settings.MaxFileBytes, budget, token);
        var memoryText = isRoot
            ? ScaleServerCgroupHierarchy.ReadRootLimit(memoryPath)
            : await BoundedText.ReadAsync(memoryPath, budget.Settings.MaxFileBytes, budget, token);
        var setText = await BoundedText.ReadAsync(Path.Combine(directory, Path2Text),
            budget.Settings.MaxFileBytes, budget, token);
        return cpuText is null || memoryText is null || string.IsNullOrWhiteSpace(setText)
            ? null : (cpuText, memoryText, setText);
    }

    private static bool TryAccumulateCpuLimit(string cpuText, bool isRoot, ref decimal? cpu)
    {
        var parts = cpuText.Split(WhitespaceSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (isRoot && parts.Length == PeriodFieldIndex && parts[NoLimit] == Unlimited)
        {
            return true;
        }

        if (parts.Length != CpuLimitFieldCount || !long.TryParse(parts[PeriodFieldIndex], NumberStyles.None, CultureInfo.InvariantCulture,
                out var period) || period <= NoLimit)
        {
            return false;
        }

        if (parts[NoLimit] == Unlimited)
        {
            return true;
        }

        if (!long.TryParse(parts[NoLimit], NumberStyles.None, CultureInfo.InvariantCulture, out var quota) || quota <= NoLimit)
        {
            return false;
        }

        var cores = (decimal)quota / period;
        cpu = cpu is null ? cores : Math.Min(cpu.Value, cores);
        return true;
    }

    private static bool TryAccumulateMemoryLimit(string memoryText, ref long? memory)
    {
        var limitText = memoryText.Trim();
        if (limitText == Unlimited)
        {
            return true;
        }

        if (!long.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) || limit <= NoLimit)
        {
            return false;
        }

        memory = memory is null ? limit : Math.Min(memory.Value, limit);
        return true;
    }
}
