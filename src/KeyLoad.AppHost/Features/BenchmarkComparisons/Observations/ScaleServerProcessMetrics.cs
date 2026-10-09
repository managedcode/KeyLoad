using System.Globalization;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

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

        var text = await BoundedText.ReadAsync($"{ProcDirectoryPrefix}{pid}{CgroupSuffix}", budget.Settings.MaxFileBytes, budget, token);
        var row = text?.Split(LineFeedCharacter).FirstOrDefault(line => line.StartsWith(UnifiedCgroupMarker, StringComparison.Ordinal));
        return row?[UnifiedCgroupMarker.Length..].Trim();
    }

    internal static async Task<int[]?> ReadPidsAsync(string path, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const char LineFeedCharacter = '\n';

        var text = await BoundedText.ReadAsync(path, budget.Settings.MaxFileBytes, budget, token);
        if (text is null)
        {
            return null;
        }

        var values = new List<int>();
        foreach (var line in text.Split(LineFeedCharacter, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            {
                return null;
            }

            values.Add(pid);
            if (values.Count > budget.Settings.MaxProcesses)
            {
                return null;
            }
        }
        return values.ToArray();
    }

    internal static async Task<long?> ReadRssAsync(int[] pids, string expectedCgroup,
        ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const int TotalInitialValue = 0;

        long total = TotalInitialValue;
        foreach (var pid in pids)
        {
            var before = await ProcessIdentity.ReadAsync(pid, expectedCgroup, budget, token);
            var status = await BoundedText.ReadAsync($"{ProcDirectoryPrefix}{pid}{ProcessStatusSuffix}", budget.Settings.MaxFileBytes, budget, token);
            var after = await ProcessIdentity.ReadAsync(pid, expectedCgroup, budget, token);
            if (before is null || after is null || before != after || !TryRss(status, out var rss))
            {
                return null;
            }

            total = checked(total + rss);
        }
        return total;
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

internal static class ProcessIdentity
{
    private const string Proc = "/proc";
    private const string Cgroup = "/cgroup";
    private const string Stat = "/stat";
    private const string Unified = "0::";

    internal static async Task<string?> ReadAsync(int pid, string expectedCgroup, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const string PathText = "/";
        const char LineFeedCharacter = '\n';
        const char ValueCharacter = ')';
        const int BoundaryValue = 0;
        const int SecondIndex = 1;
        const char SpaceCharacter = ' ';
        const int ReadAsyncBoundaryValue = 19;
        const int ElementIndex = 19;

        var cgroup = await BoundedText.ReadAsync(Proc + PathText + pid + Cgroup, budget.Settings.MaxFileBytes, budget, token);
        var row = cgroup?.Split(LineFeedCharacter).FirstOrDefault(line => line.StartsWith(Unified, StringComparison.Ordinal));
        var stat = await BoundedText.ReadAsync(Proc + PathText + pid + Stat, budget.Settings.MaxFileBytes, budget, token);
        if (row is null || row[Unified.Length..].Trim() != expectedCgroup || stat is null)
        {
            return null;
        }

        var close = stat.LastIndexOf(ValueCharacter);
        var fields = close < BoundaryValue ? [] : stat[(close + SecondIndex)..].Split(SpaceCharacter, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length <= ReadAsyncBoundaryValue ? null : fields[ElementIndex];
    }
}
