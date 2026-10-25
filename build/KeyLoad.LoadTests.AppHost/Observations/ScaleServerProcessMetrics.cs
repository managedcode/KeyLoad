using System.Globalization;

namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

/// <summary>Reads bounded Linux process metrics while checking process and cgroup identity.</summary>
internal static class ScaleServerProcessMetrics
{
    private const string ProcDirectoryPrefix = "/proc/";
    private const string CgroupSuffix = "/cgroup";
    private const string ProcessStatusSuffix = "/status";
    private const string UnifiedCgroupMarker = "0::";
    private const string RssPrefix = "VmRSS:";
    private const string UsagePrefix = "usage_usec ";
    internal static async Task<string?> ReadCgroupPathAsync(int pid, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const char LineFeedCharacter = '\n';

        var text = (await BoundedText.ReadAsync($"{ProcDirectoryPrefix}{pid}{CgroupSuffix}", budget.Settings.MaxFileBytes, budget, token)).Value;
        var row = text?.Split(LineFeedCharacter).FirstOrDefault(line => line.StartsWith(UnifiedCgroupMarker, StringComparison.Ordinal));
        return row?[UnifiedCgroupMarker.Length..].Trim();
    }

    internal static async Task<global::ManagedCode.Communication.Result<int[]?>> ReadPidsAsync(string path, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const char LineFeedCharacter = '\n';

        var text = (await BoundedText.ReadAsync(path, budget.Settings.MaxFileBytes, budget, token)).Value;
        if (text is null)
        {
            return global::ManagedCode.Communication.Result<int[]?>.FailNotFound(nameof(ReadPidsAsync));
        }

        var values = new List<int>();
        foreach (var line in text.Split(LineFeedCharacter, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            {
                return global::ManagedCode.Communication.Result<int[]?>.FailNotFound(nameof(ReadPidsAsync));
            }

            values.Add(pid);
            if (values.Count > budget.Settings.MaxProcesses)
            {
                return global::ManagedCode.Communication.Result<int[]?>.FailNotFound(nameof(ReadPidsAsync));
            }
        }
        return global::ManagedCode.Communication.Result<int[]?>.Succeed(((int[]?)(values.ToArray())));
    }

    internal static async Task<global::ManagedCode.Communication.Result<long?>> ReadRssAsync(int[] pids, string expectedCgroup,
        ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const int TotalInitialValue = 0;

        long total = TotalInitialValue;
        foreach (var pid in pids)
        {
            var before = (await ProcessIdentity.ReadAsync(pid, expectedCgroup, budget, token)).Value;
            var status = (await BoundedText.ReadAsync($"{ProcDirectoryPrefix}{pid}{ProcessStatusSuffix}", budget.Settings.MaxFileBytes, budget, token)).Value;
            var after = (await ProcessIdentity.ReadAsync(pid, expectedCgroup, budget, token)).Value;
            if (before is null || after is null || before != after || !TryRss(status, out var rss))
            {
                return global::ManagedCode.Communication.Result<long?>.FailNotFound(nameof(ReadRssAsync));
            }

            total = checked(total + rss);
        }
        return global::ManagedCode.Communication.Result<long?>.Succeed(((long?)(total)));
    }

    private static bool TryRss(string? status, out long bytes)
    {
        const int StructuralValue = 0;
        const char LineFeedCharacter = '\n';
        const char SpaceCharacter = ' ';
        const int ScaleFactor = 1024;
        const int BoundaryValue = 0;

        bytes = StructuralValue;
        var row = status?.Split(LineFeedCharacter).FirstOrDefault(line => line.StartsWith(RssPrefix, StringComparison.Ordinal));
        var value = row?[RssPrefix.Length..].Trim().Split(SpaceCharacter).FirstOrDefault();
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var kb)
            && (bytes = checked(kb * ScaleFactor)) >= BoundaryValue;
    }

    internal static bool TryUsage(string stats, out long usage)
    {
        const char LineFeedCharacter = '\n';

        var row = stats.Split(LineFeedCharacter).FirstOrDefault(line => line.StartsWith(UsagePrefix, StringComparison.Ordinal));
        return long.TryParse(row?[UsagePrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out usage);
    }

    internal static bool TryMemoryCurrent(string text, out long bytes)
    {
        const long MinimumMemoryBytes = 0;
        return long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out bytes)
            && bytes >= MinimumMemoryBytes;
    }

}
