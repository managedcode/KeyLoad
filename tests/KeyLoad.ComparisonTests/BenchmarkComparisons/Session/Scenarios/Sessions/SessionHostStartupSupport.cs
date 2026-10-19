using SessionFields = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.BenchmarkSessionEvidenceKeys;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class SessionHostStartupSupport
{
    internal const string Invalid = "IsolatedComparisonHostFailed";
    internal const string Session = "Benchmarks__Session";
    internal const string Target = "Benchmarks__Target";
    internal const string Nodes = "Benchmarks__NodeCount";
    internal const string Endpoints = "Benchmarks__Execution__Endpoints__";
    internal const string Connection = "Benchmarks__Execution__ConnectionString";
    internal const string Image = "Benchmarks__Execution__Image";
    internal const string User = "Benchmarks__Execution__User";
    internal const string Password = "Benchmarks__Execution__Password";
    internal const string Canary = "execution-session-private-canary";
    internal static readonly string[] RuntimeNames =
        ["LoadMillion", "BuildIndexes", "SequentialRead", "RandomRead", "Search", "Mixed", "VerifyFinalState"];

    internal static Dictionary<string, string> ValidSettings() => new(StringComparer.OrdinalIgnoreCase)
    {
        [Session] = "true",
        [Target] = "KeyLoad",
        [Nodes] = "3",
        [SessionFields.SessionEnvironment] = "keyload-n3",
        [SessionFields.ProfileEnvironment] = "session",
        [SessionFields.SourceEnvironment] = ComparisonExecutionIdentitySupport.Revision,
        [SessionFields.AdminEnvironment] = Canary,
        [SessionFields.OutputEnvironment] = Path.Combine(SessionNodeProcess.RepositoryRoot(), "artifacts", "qualification",
            "session-startup-" + Guid.NewGuid().ToString("N")),
        [SessionFields.StorageEnvironment] = "execution-session-startup-contract",
        [Endpoints + "0"] = "http://127.0.0.1:1",
        [Endpoints + "1"] = "http://127.0.0.1:2",
        [Endpoints + "2"] = "http://127.0.0.1:3",
        [ComparisonExecutionIdentitySupport.GitHubActions] = "true",
        [SessionFields.RunEnvironment] = "37070000000",
        [SessionFields.AttemptEnvironment] = "1",
        [SessionFields.RepositoryEnvironment] = "managedcode/KeyLoad",
        [SessionFields.ReferenceEnvironment] = "refs/heads/main",
        [SessionFields.WorkflowEnvironment] = "Benchmarks",
        [SessionFields.ShaEnvironment] = ComparisonExecutionIdentitySupport.Revision,
        [SessionFields.JobEnvironment] = "123"
    };

    internal static async Task AssertRefusedAsync(Dictionary<string, string> settings, string detail)
    {
        var output = settings[SessionFields.OutputEnvironment];
        var result = await ComparisonHostProcess.RunAsync([], settings, TestContext.Current!.Execution.CancellationToken);
        var text = result.Stdout + result.Stderr;
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(text).Contains(detail);
        await Assert.That(text).Contains(RuntimeNames[0]);
        await Assert.That(text.Contains(Canary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(File.Exists(Path.Combine(output, "reports", "load-million.json"))).IsFalse();
    }
}
