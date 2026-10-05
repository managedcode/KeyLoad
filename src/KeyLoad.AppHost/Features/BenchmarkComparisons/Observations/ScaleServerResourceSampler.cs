using System.Globalization;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed class ScaleServerResourceSampler(string resourceName, string[] expectedMountTargets)
{
    private const string CgroupRoot = "/sys/fs/cgroup";
    private const string CgroupFile = "cgroup.procs";
    private const string CpuLimitFile = "cpu.max";
    private const string CpuStatsFile = "cpu.stat";
    private const string MemoryLimitFile = "memory.max";
    private const string MemoryCurrentFile = "memory.current";
    private const string UnifiedCgroupMarker = "0::";
    private const string RssPrefix = "VmRSS:";
    private const string UsagePrefix = "usage_usec ";
    private string? _containerId;
    private string? _imageId;
    private string? _startedAt;
    private string? _state;
    private string? _path;
    private string? _cpuLimit;
    private string? _memoryLimit;
    private long _firstCpuUsec;
    private long _lastCpuUsec;
    private long _maxRss;
    private long _maxCgroupMemory;
    private int _samples;
    private bool _invalid;
    private bool _readyBoundary;
    private bool _mountsObserved;
    private string? _mountDescriptor;
    private ScaleServerEnvelope? _effectiveEnvelope;
    private ScaleServerMount[] _mounts = [];

    internal string ResourceName => resourceName;
    internal bool Invalidated => _invalid;
    internal bool StorageUnavailable { get; private set; }

    internal void MarkReadyBoundary() => _readyBoundary = true;

    internal async Task<bool> SampleAsync(ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        var text = await ScaleServerResourceProcess.InspectAsync(resourceName, budget, token);
        if (text is null) return InvalidateAfterStart();
        var fields = text.Split('|', StringSplitOptions.None);
        if (fields.Length != 6 || !int.TryParse(fields[2], out var initPid) || initPid <= 0) return InvalidateAfterStart();
        if (_containerId is not null && (_containerId != fields[0] || _imageId != fields[1] || _startedAt != fields[3]))
            return InvalidateAfterStart();
        _containerId = fields[0];
        _imageId = fields[1];
        _startedAt = fields[3];
        _state = fields[4];
        if (_state != "running") return InvalidateAfterStart();
        return await SampleRunningAsync(initPid, fields[5], budget, token);
    }

    private async Task<bool> SampleRunningAsync(int initPid, string mountDescriptor,
        ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        _path = await ReadCgroupPathAsync(initPid, budget, token);
        if (_path is null) return InvalidateAfterStart();
        var effectiveEnvelope = await ScaleServerHostEvidence.ReadCgroupEnvelopeAsync(_path, token, budget);
        if (effectiveEnvelope is null || _effectiveEnvelope is not null && _effectiveEnvelope != effectiveEnvelope)
            return InvalidateAfterStart();
        _effectiveEnvelope = effectiveEnvelope;
        var directory = Path.GetFullPath(Path.Combine(CgroupRoot, _path.TrimStart('/')));
        if (!directory.StartsWith(CgroupRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return InvalidateAfterStart();
        if (Directory.EnumerateDirectories(directory).Any()) return InvalidateAfterStart();
        var pids = await ReadPidsAsync(Path.Combine(directory, CgroupFile), budget, token);
        if (pids is null || pids.Length is 0 or > ScaleServerResourceBounds.MaxProcesses) return InvalidateAfterStart();
        Array.Sort(pids);
        var rss = await ReadRssAsync(pids, _path, budget, token);
        if (rss is null) return InvalidateAfterStart();
        var finalPids = await ReadPidsAsync(Path.Combine(directory, CgroupFile), budget, token);
        if (finalPids is null) return InvalidateAfterStart();
        Array.Sort(finalPids);
        if (!pids.SequenceEqual(finalPids)) return InvalidateAfterStart();
        return await CommitSampleAsync(directory, mountDescriptor, rss.Value, budget, token);
    }

    private async Task<bool> CommitSampleAsync(string directory, string mountDescriptor, long rss,
        ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        var cpuLimit = await BoundedText.ReadAsync(Path.Combine(directory, CpuLimitFile), ScaleServerResourceBounds.MaxFileBytes, token, budget);
        var memoryLimit = await BoundedText.ReadAsync(Path.Combine(directory, MemoryLimitFile), ScaleServerResourceBounds.MaxFileBytes, token, budget);
        var cpuStats = await BoundedText.ReadAsync(Path.Combine(directory, CpuStatsFile), ScaleServerResourceBounds.MaxFileBytes, token, budget);
        var memoryCurrent = await BoundedText.ReadAsync(Path.Combine(directory, MemoryCurrentFile), ScaleServerResourceBounds.MaxFileBytes, token, budget);
        if (cpuLimit is null || memoryLimit is null || cpuStats is null || memoryCurrent is null)
            return InvalidateAfterStart();
        if (!TryUsage(cpuStats, out var usage)
            || !long.TryParse(memoryCurrent, NumberStyles.None, CultureInfo.InvariantCulture, out var currentMemory)
            || _samples > 0 && usage < _lastCpuUsec)
            return InvalidateAfterStart();
        if (_samples == 0) _firstCpuUsec = usage;
        _lastCpuUsec = usage;
        var nextCpuLimit = cpuLimit.Trim();
        var nextMemoryLimit = memoryLimit.Trim();
        if (_cpuLimit is not null && (_cpuLimit != nextCpuLimit || _memoryLimit != nextMemoryLimit))
            return InvalidateAfterStart();
        _cpuLimit = nextCpuLimit;
        _memoryLimit = nextMemoryLimit;
        _maxRss = Math.Max(_maxRss, rss);
        _maxCgroupMemory = Math.Max(_maxCgroupMemory, currentMemory);
        if (!_mountsObserved)
        {
            _mounts = await ScaleServerMountReader.ReadAsync(mountDescriptor, expectedMountTargets, budget, token);
            _mountDescriptor = mountDescriptor;
            _mountsObserved = true;
        }
        else if (!string.Equals(_mountDescriptor, mountDescriptor, StringComparison.Ordinal))
        {
            StorageUnavailable = true;
            return InvalidateAfterStart();
        }
        if (_mounts.Length != expectedMountTargets.Length)
        {
            StorageUnavailable = true;
            return InvalidateAfterStart();
        }
        _samples++;
        return _mounts.Length > 0 || InvalidateAfterStart();
    }

    private bool InvalidateAfterStart()
    {
        if (_readyBoundary || _containerId is not null) _invalid = true;
        return false;
    }

    internal ScaleServerContainer? ToRecord()
        => _samples == 0 || _containerId is null || _imageId is null || _startedAt is null || _state is null
            || _cpuLimit is null || _memoryLimit is null ? null
            : new(resourceName, _containerId, _imageId, _startedAt, _state, _cpuLimit, _memoryLimit,
                _effectiveEnvelope!.CpuQuota, _effectiveEnvelope.CpuSet, _effectiveEnvelope.MemoryLimitBytes,
                checked(_lastCpuUsec - _firstCpuUsec), _maxCgroupMemory, _maxRss, _samples, _mounts);

    private static async Task<string?> ReadCgroupPathAsync(int pid, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        var text = await BoundedText.ReadAsync($"/proc/{pid}/cgroup", ScaleServerResourceBounds.MaxFileBytes, token, budget);
        var row = text?.Split('\n').FirstOrDefault(line => line.StartsWith(UnifiedCgroupMarker, StringComparison.Ordinal));
        return row is null ? null : row[UnifiedCgroupMarker.Length..].Trim();
    }

    private static async Task<int[]?> ReadPidsAsync(string path, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        var text = await BoundedText.ReadAsync(path, ScaleServerResourceBounds.MaxFileBytes, token, budget);
        if (text is null) return null;
        var values = new List<int>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) return null;
            values.Add(pid);
            if (values.Count > ScaleServerResourceBounds.MaxProcesses) return null;
        }
        return values.ToArray();
    }

    private static async Task<long?> ReadRssAsync(int[] pids, string expectedCgroup,
        ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        long total = 0;
        foreach (var pid in pids)
        {
            var before = await ProcessIdentity.ReadAsync(pid, expectedCgroup, budget, token);
            var status = await BoundedText.ReadAsync($"/proc/{pid}/status", ScaleServerResourceBounds.MaxFileBytes, token, budget);
            var after = await ProcessIdentity.ReadAsync(pid, expectedCgroup, budget, token);
            if (before is null || after is null || before != after || !TryRss(status, out var rss)) return null;
            total = checked(total + rss);
        }
        return total;
    }

    private static bool TryRss(string? status, out long bytes)
    {
        bytes = 0;
        var row = status?.Split('\n').FirstOrDefault(line => line.StartsWith(RssPrefix, StringComparison.Ordinal));
        var value = row is null ? null : row[RssPrefix.Length..].Trim().Split(' ').FirstOrDefault();
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var kb)
            && (bytes = checked(kb * 1024)) >= 0;
    }

    private static bool TryUsage(string stats, out long usage)
    {
        var row = stats.Split('\n').FirstOrDefault(line => line.StartsWith(UsagePrefix, StringComparison.Ordinal));
        return long.TryParse(row?[UsagePrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out usage);
    }

}

internal static class ProcessIdentity
{
    private const string Proc = "/proc";
    private const string Cgroup = "/cgroup";
    private const string Stat = "/stat";
    private const string Unified = "0::";

    internal static async Task<string?> ReadAsync(int pid, string expectedCgroup, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        var cgroup = await BoundedText.ReadAsync(Proc + "/" + pid + Cgroup, ScaleServerResourceBounds.MaxFileBytes, token, budget);
        var row = cgroup?.Split('\n').FirstOrDefault(line => line.StartsWith(Unified, StringComparison.Ordinal));
        var stat = await BoundedText.ReadAsync(Proc + "/" + pid + Stat, ScaleServerResourceBounds.MaxFileBytes, token, budget);
        if (row is null || row[Unified.Length..].Trim() != expectedCgroup || stat is null) return null;
        var close = stat.LastIndexOf(')');
        string[] fields = close < 0 ? [] : stat[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length <= 19 ? null : fields[19];
    }
}
