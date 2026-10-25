using System.Globalization;

namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

internal static class ScaleServerCgroupEnvelopeReader
{
    private const char WhitespaceSeparator = ' ';
    private const int CpuLimitFieldCount = 2;

    private const string CpuMaximum = "cpu.max";
    private const string MemoryMaximum = "memory.max";
    private const string Unlimited = "max";

    internal static async Task<(bool HasValue, ScaleServerEnvelope? Value)> ReadFromDirectoryAsync(string current, string root,
        ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const int DepthInitialValue = 0;

        if (!await ScaleServerCgroupHierarchy.IsSupportedRootAsync(root, budget, token))
        {
            return (false, default(ScaleServerEnvelope?));
        }

        decimal? cpu = null;
        long? memory = null;
        string? cpuSet = null;
        var directory = current;
        for (var depth = DepthInitialValue; depth < budget.Settings.MaxCgroupAncestors; depth++)
        {
            var isRoot = directory == root;
            var inputs = (await ReadAncestorInputsAsync(directory, isRoot, budget, token)).Value;
            if (inputs is null)
            {
                return (false, default(ScaleServerEnvelope?));
            }

            if (cpuSet is null)
            {
                cpuSet = inputs.Value.CpuSet.Trim();
            }

            if (!TryAccumulateCpuLimit(inputs.Value.Cpu, isRoot, ref cpu)
                || !TryAccumulateMemoryLimit(inputs.Value.Memory, ref memory))
            {
                return (false, default(ScaleServerEnvelope?));
            }

            if (directory == root)
            {
                break;
            }

            directory = Path.GetDirectoryName(directory) ?? root;
        }
        if (directory != root)
        {
            return (false, default(ScaleServerEnvelope?));
        }

        var cpuValue = cpu?.ToString(CultureInfo.InvariantCulture) ?? Unlimited;
        var memoryValue = memory?.ToString(CultureInfo.InvariantCulture) ?? Unlimited;
        return cpuSet is null ? (false, default(ScaleServerEnvelope?)) : (true, (ScaleServerEnvelope?)(new ScaleServerEnvelope(cpuValue, cpuSet, memoryValue)));
    }

    private static async Task<(bool HasValue, (string Cpu, string Memory, string CpuSet)? Value)> ReadAncestorInputsAsync(
        string directory, bool isRoot, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const string EffectiveCpuSetFileName = "cpuset.cpus.effective";

        var cpuPath = Path.Combine(directory, CpuMaximum);
        var memoryPath = Path.Combine(directory, MemoryMaximum);
        var cpuText = isRoot
            ? (ScaleServerCgroupHierarchy.ReadRootLimit(cpuPath)).Value
            : (await BoundedText.ReadAsync(cpuPath, budget.Settings.MaxFileBytes, budget, token)).Value;
        var memoryText = isRoot
            ? (ScaleServerCgroupHierarchy.ReadRootLimit(memoryPath)).Value
            : (await BoundedText.ReadAsync(memoryPath, budget.Settings.MaxFileBytes, budget, token)).Value;
        var setText = (await BoundedText.ReadAsync(Path.Combine(directory, EffectiveCpuSetFileName),
            budget.Settings.MaxFileBytes, budget, token)).Value;
        return cpuText is null || memoryText is null || string.IsNullOrWhiteSpace(setText)
            ? (false, default((string Cpu, string Memory, string CpuSet)?)) : (true, (((string Cpu, string Memory, string CpuSet)?)((cpuText, memoryText, setText))));
    }

    internal static bool TryAccumulateCpuLimit(string cpuText, bool isRoot, ref decimal? cpu)
    {
        var parts = cpuText.Trim().Split(WhitespaceSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (isRoot && parts.Length == 1 && parts[0] == Unlimited)
        {
            return true;
        }

        if (parts.Length != CpuLimitFieldCount || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                out var period) || period <= 0)
        {
            return false;
        }

        if (parts[0] == Unlimited)
        {
            return true;
        }

        if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var quota) || quota <= 0)
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

        if (!long.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) || limit <= 0)
        {
            return false;
        }

        memory = memory is null ? limit : Math.Min(memory.Value, limit);
        return true;
    }
}
