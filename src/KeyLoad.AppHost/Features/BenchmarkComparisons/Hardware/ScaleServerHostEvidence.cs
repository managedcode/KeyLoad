using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;

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
        IOptions<ScaleServerResourceOptions> settings, IOptions<BenchmarkProvenanceOptions> provenance, CancellationToken token)
    {
        const string KernelReleaseArgument = "-r";
        const int BoundaryValue = 1;
        const char SeparatorCharacter = ',';

        if (!OperatingSystem.IsLinux())
        {
            return (null, null);
        }

        var budget = new ScaleServerResourceSampleBudget(settings, provenance);
        var cpu = await BoundedText.ReadAsync(CpuInfo, budget.Settings.MaxHardwareBytes, budget, token);
        var memory = await BoundedText.ReadAsync(MemoryInfo, budget.Settings.MaxFileBytes, budget, token);
        var online = await BoundedText.ReadAsync(CpuOnline, budget.Settings.MaxFileBytes, budget, token);
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

        var kernel = await ScaleServerResourceProcess.RunAsync(KernelExecutable, [KernelReleaseArgument], budget, token);
        if (string.IsNullOrWhiteSpace(kernel))
        {
            return (null, null);
        }

        var logical = ScaleServerHostCpuParser.CountOnline(online);
        var cores = ScaleServerHostCpuParser.CoreMembership(cpu);
        var envelope = await ReadEnvelopeAsync(budget, token);
        if (logical < BoundaryValue || cores.Physical.Length < BoundaryValue || cores.Logical.Split(SeparatorCharacter).Length != logical)
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
        const int EmptyValue = 0;
        const int IndexValue = 0;
        const char SlashCharacter = '/';
        const string UnifiedCgroupMountPath = "/sys/fs/cgroup";

        if (!OperatingSystem.IsLinux() || cgroupPath.Length == EmptyValue || cgroupPath[IndexValue] != SlashCharacter)
        {
            return null;
        }

        var root = Path.GetFullPath(UnifiedCgroupMountPath);
        var current = Path.GetFullPath(Path.Combine(root, cgroupPath.TrimStart(SlashCharacter)));
        if (current != root && !current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        return await ScaleServerCgroupEnvelopeReader.ReadFromDirectoryAsync(current, root, budget, token);
    }

    private static async Task<ScaleServerEnvelope?> ReadEnvelopeAsync(ScaleServerResourceSampleBudget budget,
        CancellationToken token)
    {
        const string ProcessCgroupMembershipPath = "/proc/self/cgroup";
        const char LineFeedCharacter = '\n';
        const string CgroupDirectoryMountPath = "/sys/fs/cgroup";
        const int ElementIndex = 3;
        const char SlashCharacter = '/';
        const string CgroupHierarchyRootPath = "/sys/fs/cgroup";

        var membership = await BoundedText.ReadAsync(ProcessCgroupMembershipPath, budget.Settings.MaxFileBytes, budget, token);
        var row = membership?.Split(LineFeedCharacter).FirstOrDefault(line => line.StartsWith(UnifiedMembershipPrefix, StringComparison.Ordinal));
        if (row is null)
        {
            return null;
        }

        if (!row.StartsWith(UnifiedAbsoluteMembershipPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var current = Path.GetFullPath(Path.Combine(CgroupDirectoryMountPath, row[ElementIndex..].TrimStart(SlashCharacter)));
        var root = Path.GetFullPath(CgroupHierarchyRootPath);
        if (current != root && !current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        return await ScaleServerCgroupEnvelopeReader.ReadFromDirectoryAsync(current, root, budget, token);
    }
}
