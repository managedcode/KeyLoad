namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Builds real child-process settings for the separate TimeSeries host identity.</summary>
internal static class ComparisonExecutionIdentityTimeSeriesSupport
{
    internal const string Profile = "Benchmarks__Profile";
    internal const string EvidenceProfile = "Benchmarks__EvidenceProfile";
    internal const string SourceRevision = "Benchmarks__SourceRevision";
    internal const string GitHubSha = "GITHUB_SHA";
    internal const string LoadGeneratorImage = "Benchmarks__LoadGeneratorImage";
    internal const string TimescaleImage = "Benchmarks__Images__Timescale";
    internal const string KeyLoadImage = "Benchmarks__Images__KeyLoad";
    internal const string TimescaleConnectionSetting = "ConnectionStrings__benchmark-timescale";
    internal const string InvalidCode = "ComparisonExecutionIdentityInvalid";
    internal const string MissingPrefix = "Missing benchmark setting: ";

    internal static Dictionary<string, string> ValidSettings(string evidenceProfile = "timeseries") => new(StringComparer.OrdinalIgnoreCase)
    {
        [Profile] = "timeseries",
        [ComparisonHostBindingsSupport.KeyLoadEndpoint] = "http://127.0.0.1:1",
        [ComparisonHostBindingsSupport.AdminKeySetting] = "timeseries-admin-sentinel",
        [TimescaleConnectionSetting] = "Host=127.0.0.1;Port=0;Username=benchmark;Password=timeseries-db-sentinel;Database=benchmark;Timeout=1",
        [TimescaleImage] = "timescale/timescaledb:2.30.2-pg18@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        [KeyLoadImage] = ComparisonExecutionIdentitySupport.KeyLoadImageValue,
        [LoadGeneratorImage] = ComparisonExecutionIdentitySupport.LoadGeneratorImageValue,
        [ComparisonHostBindingsSupport.OutputSetting] = Path.GetTempPath(),
        [ComparisonHostBindingsSupport.StorageSetting] = "temporary-timeseries-process-test",
        [SourceRevision] = ComparisonExecutionIdentitySupport.Revision,
        [EvidenceProfile] = evidenceProfile,
        [ComparisonExecutionIdentitySupport.RunId] = "37070000000",
        [ComparisonExecutionIdentitySupport.RunAttempt] = "1",
        [ComparisonExecutionIdentitySupport.Repository] = "managedcode/KeyLoad",
        [ComparisonExecutionIdentitySupport.GitHubRef] = "refs/heads/main",
        [ComparisonExecutionIdentitySupport.Workflow] = "CI",
        [GitHubSha] = ComparisonExecutionIdentitySupport.Revision
    };

    internal static Task<ComparisonHostExit> RunAsync(Dictionary<string, string> settings)
        => ComparisonHostProcess.RunAsync([], settings, TestContext.Current!.Execution.CancellationToken);

    internal static async Task AssertSafeFailureAsync(ComparisonHostExit result, string expectedDetail,
        params string[] privateValues)
    {
        var output = result.Stdout + result.Stderr;
        foreach (var secret in new[] { "timeseries-admin-sentinel", "timeseries-db-sentinel" })
        {
            await Assert.That(output.Contains(secret, StringComparison.Ordinal)).IsFalse();
        }

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result, expectedDetail, privateValues);
    }
}
