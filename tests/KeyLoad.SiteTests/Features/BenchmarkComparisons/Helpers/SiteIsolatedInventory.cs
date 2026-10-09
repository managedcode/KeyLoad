namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Current authenticated comparison inventory only.</summary>
internal static class SiteIsolatedInventory
{
    private const int CurrentControlWorkers = 220;

    internal static int Workers() => SiteIsolatedGitHubTokens.WorkerCount;

    internal static int ControlWorkers() => CurrentControlWorkers;
}
