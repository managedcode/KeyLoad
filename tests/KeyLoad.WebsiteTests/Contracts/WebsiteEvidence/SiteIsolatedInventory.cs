namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

/// <summary>Current planned comparison inventory; measured sessions are counted from original receipts.</summary>
internal static class SiteIsolatedInventory
{
    internal static int Workers() => SiteIsolatedGitHubTokens.WorkerCount;
}
