using System.Globalization;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-002/006: real host configuration and exclusively owned temporary output.</summary>
internal sealed class IsolatedHostFixture : IDisposable
{
    internal const string Failure = "IsolatedComparisonHostFailed";
    internal const string Canary = "private-isolated-host-canary";
    internal const string Target = "Benchmarks__Target";
    internal const string Nodes = "Benchmarks__NodeCount";
    internal const string Scenario = "Benchmarks__Scenario";
    internal const string Image = "Benchmarks__Native__Image";
    internal const string EndpointPrefix = "Benchmarks__Native__Endpoints__";
    internal const string Endpoints = "Benchmarks__Native__Endpoints";
    internal const string JobId = "KEYLOAD_COMPARISON_JOB_ID";
    internal const string Output = "Benchmarks__Output";
    internal const string Storage = "Benchmarks__Storage";
    internal const string Profile = "Benchmarks__Profile";
    internal const string Connection = "Benchmarks__Native__ConnectionString";
    internal const string AdminKey = "Benchmarks__AdminKey";
    internal const string User = "Benchmarks__Native__User";
    internal const string Password = "Benchmarks__Native__Password";
    internal readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "keyload-isolated-host-" + Guid.NewGuid().ToString("N"));

    internal Dictionary<string, string> Settings(string target = "Neo4j", int nodes = 2)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            [Target] = target,
            [Nodes] = nodes.ToString(CultureInfo.InvariantCulture),
            [Scenario] = "PointRead",
            [Image] = ComparisonExecutionIdentitySupport.KeyLoadImageValue,
            [ComparisonExecutionIdentitySupport.LoadGeneratorImage] = ComparisonExecutionIdentitySupport.LoadGeneratorImageValue,
            [ComparisonExecutionIdentitySupport.KeyLoadImage] = ComparisonExecutionIdentitySupport.KeyLoadImageValue,
            [ComparisonExecutionIdentitySupport.SourceRevision] = ComparisonExecutionIdentitySupport.Revision,
            [ComparisonExecutionIdentitySupport.EvidenceProfile] = "intensive-1k-c16",
            [ComparisonExecutionIdentitySupport.RunId] = "37070000000",
            [ComparisonExecutionIdentitySupport.RunAttempt] = "2",
            [ComparisonExecutionIdentitySupport.Repository] = "managedcode/KeyLoad",
            [ComparisonExecutionIdentitySupport.GitHubRef] = "refs/heads/main",
            [ComparisonExecutionIdentitySupport.Workflow] = "CI",
            [ComparisonExecutionIdentitySupport.GitHubSha] = ComparisonExecutionIdentitySupport.Revision,
            [JobId] = "111047630080",
            [Output] = DirectoryPath,
            [Storage] = "isolated-native-test"
        };

    internal static Task<ComparisonHostExit> RunAsync(Dictionary<string, string> settings)
        => ComparisonHostProcess.RunAsync([], settings, TestContext.Current!.Execution.CancellationToken);

    internal async Task AssertFailureAsync(ComparisonHostExit result)
    {
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stderr.Trim()).IsEqualTo(Failure);
        await Assert.That((result.Stdout + result.Stderr).Contains(Canary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(File.Exists(Path.Combine(DirectoryPath, "worker.json"))).IsFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, true);
        }
        else if (File.Exists(DirectoryPath))
        {
            File.Delete(DirectoryPath);
        }
    }
}
