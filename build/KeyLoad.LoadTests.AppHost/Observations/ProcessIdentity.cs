namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

internal static class ProcessIdentity
{
    private const string Proc = "/proc";
    private const string Cgroup = "/cgroup";
    private const string Stat = "/stat";
    private const string Unified = "0::";

    internal static async Task<(bool HasValue, string? Value)> ReadAsync(int pid, string expectedCgroup, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const string PathText = "/";
        const char LineFeedCharacter = '\n';
        const char ValueCharacter = ')';
        const int BoundaryValue = 0;
        const int SecondIndex = 1;
        const char SpaceCharacter = ' ';
        const int ReadAsyncBoundaryValue = 19;
        const int ElementIndex = 19;

        var cgroup = (await BoundedText.ReadAsync(Proc + PathText + pid + Cgroup, budget.Settings.MaxFileBytes, budget, token)).Value;
        var row = cgroup?.Split(LineFeedCharacter).FirstOrDefault(line => line.StartsWith(Unified, StringComparison.Ordinal));
        var stat = (await BoundedText.ReadAsync(Proc + PathText + pid + Stat, budget.Settings.MaxFileBytes, budget, token)).Value;
        if (row is null || row[Unified.Length..].Trim() != expectedCgroup || stat is null)
        {
            return (false, default(string?));
        }

        var close = stat.LastIndexOf(ValueCharacter);
        var fields = close < BoundaryValue ? [] : stat[(close + SecondIndex)..].Split(SpaceCharacter, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length <= ReadAsyncBoundaryValue ? (false, default(string?)) : (true, ((string?)(fields[ElementIndex])));
    }
}
