namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Builds real child-process settings for the container execution identity boundary.</summary>
internal static class ComparisonExecutionIdentitySupport
{
    internal const string InvalidCode = "ComparisonExecutionIdentityInvalid";
    internal const string MissingKeyPrefix = "Missing benchmark setting: ";
    internal const string LoadGeneratorImage = "Benchmarks__LoadGeneratorImage";
    internal const string KeyLoadImage = "Benchmarks__Images__KeyLoad";
    internal const string SourceRevision = "Benchmarks__SourceRevision";
    internal const string EvidenceProfile = "Benchmarks__EvidenceProfile";
    internal const string Topology = "Benchmarks__Topology";
    internal const string RunId = "GITHUB_RUN_ID";
    internal const string RunAttempt = "GITHUB_RUN_ATTEMPT";
    internal const string Repository = "GITHUB_REPOSITORY";
    internal const string GitHubRef = "GITHUB_REF";
    internal const string Workflow = "GITHUB_WORKFLOW";
    internal const string GitHubSha = "GITHUB_SHA";
    internal const string Revision = "0123456789abcdef0123456789abcdef01234567";
    internal const string OtherRevision = "fedcba9876543210fedcba9876543210fedcba98";
    internal const string ImageDigest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    internal const string KeyLoadImageValue = "127.0.0.1:5000/keyload/server:0123456789ab-37070000000-1@sha256:" + ImageDigest;
    internal const string LoadGeneratorImageValue = "127.0.0.1:5000/keyload/comparisons:0123456789ab-37070000000-1@sha256:" + ImageDigest;
    internal const string PrivateCanary = "private-provenance-canary-6af143";

    internal static Dictionary<string, string> ValidSettings(string profile = "smoke-single")
    {
        var settings = ComparisonHostBindingsSupport.ValidSettings();
        settings[LoadGeneratorImage] = LoadGeneratorImageValue;
        settings[KeyLoadImage] = KeyLoadImageValue;
        settings[SourceRevision] = Revision;
        settings[EvidenceProfile] = profile;
        settings[RunId] = "37070000000";
        settings[RunAttempt] = "1";
        settings[Repository] = "managedcode/KeyLoad";
        settings[GitHubRef] = "refs/heads/main";
        settings[Workflow] = "CI";
        settings[GitHubSha] = Revision;
        return settings;
    }

    internal static Task<ComparisonHostExit> RunAsync(Dictionary<string, string> settings)
        => ComparisonHostProcess.RunAsync([], settings, TestContext.Current!.Execution.CancellationToken);

    internal static async Task AssertSafeFailureAsync(ComparisonHostExit result, string expectedDetail,
        params string[] privateValues)
    {
        var output = result.Stdout + result.Stderr;
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That((result.Stdout + result.Stderr).Contains(expectedDetail, StringComparison.Ordinal)).IsTrue();
        foreach (var value in privateValues)
        {
            await Assert.That(output.Contains(value, StringComparison.Ordinal)).IsFalse();
        }

        await ComparisonHostBindingsSupport.AssertSafeFailureAsync(result, expectedDetail);
    }
}
