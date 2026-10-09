namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed class ScaleServerResourceSampler(string resourceName, string[] expectedMountTargets)
{
    private const int ContainerImageFieldIndex = 4;
    private const int ContainerIncarnationFieldIndex = 5;

    private const int ToRecordEmptyValue = 0;

    private const string RunningContainerState = "running";

    private const string CgroupRoot = "/sys/fs/cgroup";
    private const string CgroupFile = "cgroup.procs";
    private const string CpuLimitFile = "cpu.max";
    private const string CpuStatsFile = "cpu.stat";
    private const string MemoryLimitFile = "memory.max";
    private const string MemoryCurrentFile = "memory.current";
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
        const char SeparatorCharacter = '|';
        const int InspectFieldCount = 6;
        const int ElementIndex = 2;
        const int BoundaryValue = 0;
        const int FirstIndex = 0;
        const int SecondIndex = 1;
        const int SampleAsyncElementIndex = 3;

        var text = await ScaleServerResourceProcess.InspectAsync(resourceName, budget, token);
        if (text is null)
        {
            return InvalidateAfterStart();
        }

        var fields = text.Split(SeparatorCharacter, StringSplitOptions.None);
        if (fields.Length != InspectFieldCount || !int.TryParse(fields[ElementIndex], out var initPid) || initPid <= BoundaryValue)
        {
            return InvalidateAfterStart();
        }

        if (_containerId is not null && (_containerId != fields[FirstIndex] || _imageId != fields[SecondIndex] || _startedAt != fields[SampleAsyncElementIndex]))
        {
            return InvalidateAfterStart();
        }

        _containerId = fields[FirstIndex];
        _imageId = fields[SecondIndex];
        _startedAt = fields[SampleAsyncElementIndex];
        _state = fields[ContainerImageFieldIndex];
        if (_state != RunningContainerState)
        {
            return InvalidateAfterStart();
        }

        return await SampleRunningAsync(initPid, fields[ContainerIncarnationFieldIndex], budget, token);
    }

    private async Task<bool> SampleRunningAsync(int initPid, string mountDescriptor,
        ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const char SlashCharacter = '/';
        const int NoProcesses = 0;

        _path = await ScaleServerProcessMetrics.ReadCgroupPathAsync(initPid, budget, token);
        if (_path is null)
        {
            return InvalidateAfterStart();
        }

        var effectiveEnvelope = await ScaleServerHostEvidence.ReadCgroupEnvelopeAsync(_path, budget, token);
        if (effectiveEnvelope is null || _effectiveEnvelope is not null && _effectiveEnvelope != effectiveEnvelope)
        {
            return InvalidateAfterStart();
        }

        _effectiveEnvelope = effectiveEnvelope;
        var directory = Path.GetFullPath(Path.Combine(CgroupRoot, _path.TrimStart(SlashCharacter)));
        if (!directory.StartsWith(CgroupRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return InvalidateAfterStart();
        }

        if (Directory.EnumerateDirectories(directory).Any())
        {
            return InvalidateAfterStart();
        }

        var pids = await ScaleServerProcessMetrics.ReadPidsAsync(Path.Combine(directory, CgroupFile), budget, token);
        if (pids is null || pids.Length == NoProcesses || pids.Length > budget.Settings.MaxProcesses)
        {
            return InvalidateAfterStart();
        }

        Array.Sort(pids);
        var rss = await ScaleServerProcessMetrics.ReadRssAsync(pids, _path, budget, token);
        if (rss is null)
        {
            return InvalidateAfterStart();
        }

        var finalPids = await ScaleServerProcessMetrics.ReadPidsAsync(Path.Combine(directory, CgroupFile), budget, token);
        if (finalPids is null)
        {
            return InvalidateAfterStart();
        }

        Array.Sort(finalPids);
        if (!pids.SequenceEqual(finalPids))
        {
            return InvalidateAfterStart();
        }

        return await CommitSampleAsync(directory, mountDescriptor, rss.Value, budget, token);
    }

    private async Task<bool> CommitSampleAsync(string directory, string mountDescriptor, long rss,
        ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const int BoundaryValue = 0;
        const int NoSamples = 0;

        var cpuLimit = await BoundedText.ReadAsync(Path.Combine(directory, CpuLimitFile), budget.Settings.MaxFileBytes, budget, token);
        var memoryLimit = await BoundedText.ReadAsync(Path.Combine(directory, MemoryLimitFile), budget.Settings.MaxFileBytes, budget, token);
        var cpuStats = await BoundedText.ReadAsync(Path.Combine(directory, CpuStatsFile), budget.Settings.MaxFileBytes, budget, token);
        var memoryCurrent = await BoundedText.ReadAsync(Path.Combine(directory, MemoryCurrentFile), budget.Settings.MaxFileBytes, budget, token);
        if (cpuLimit is null || memoryLimit is null || cpuStats is null || memoryCurrent is null)
        {
            return InvalidateAfterStart();
        }

        if (!ScaleServerProcessMetrics.TryUsage(cpuStats, out var usage)
            || !ScaleServerProcessMetrics.TryMemoryCurrent(memoryCurrent, out var currentMemory)
            || _samples > BoundaryValue && usage < _lastCpuUsec)
        {
            return InvalidateAfterStart();
        }

        if (_samples == NoSamples)
        {
            _firstCpuUsec = usage;
        }

        _lastCpuUsec = usage;
        var nextCpuLimit = cpuLimit.Trim();
        var nextMemoryLimit = memoryLimit.Trim();
        if (_cpuLimit is not null && (_cpuLimit != nextCpuLimit || _memoryLimit != nextMemoryLimit))
        {
            return InvalidateAfterStart();
        }

        _cpuLimit = nextCpuLimit;
        _memoryLimit = nextMemoryLimit;
        _maxRss = Math.Max(_maxRss, rss);
        _maxCgroupMemory = Math.Max(_maxCgroupMemory, currentMemory);
        if (!await ObserveMountsAsync(mountDescriptor, budget, token))
        {
            return InvalidateAfterStart();
        }
        _samples++;
        return _mounts.Length > BoundaryValue || InvalidateAfterStart();
    }

    private async Task<bool> ObserveMountsAsync(string mountDescriptor,
        ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        if (!_mountsObserved)
        {
            _mounts = await ScaleServerMountReader.ReadAsync(mountDescriptor, expectedMountTargets, budget, token);
            _mountDescriptor = mountDescriptor;
            _mountsObserved = true;
        }
        else if (!string.Equals(_mountDescriptor, mountDescriptor, StringComparison.Ordinal))
        {
            StorageUnavailable = true;
            return false;
        }
        if (_mounts.Length != expectedMountTargets.Length)
        {
            StorageUnavailable = true;
            return false;
        }
        return true;
    }

    private bool InvalidateAfterStart()
    {
        if (_readyBoundary || _containerId is not null)
        {
            _invalid = true;
        }

        return false;
    }

    internal ScaleServerContainer? ToRecord()
        => _samples == ToRecordEmptyValue || _containerId is null || _imageId is null || _startedAt is null || _state is null
            || _cpuLimit is null || _memoryLimit is null ? null
            : new(resourceName, _containerId, _imageId, _startedAt, _state, _cpuLimit, _memoryLimit,
                _effectiveEnvelope!.CpuQuota, _effectiveEnvelope.CpuSet, _effectiveEnvelope.MemoryLimitBytes,
                checked(_lastCpuUsec - _firstCpuUsec), _maxCgroupMemory, _maxRss, _samples, _mounts);

}
