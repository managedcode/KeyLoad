namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedGitHubNativeProtocol
{
    internal const string EnvironmentFile = "GITHUB_ENV";
    internal const string RunnerTemp = "RUNNER_TEMP";
    internal const string Capture = "keyload-cell-github";
    internal const string Job = "job.json";
    internal const string Entry = "isolated-github-job.mjs";
    internal const string Id = "id";
    internal const string Name = "name";
    internal const string Source = "head_sha";
    internal const string Run = "run_id";
    internal const string Status = "status";
    internal const string Workflow = "workflow_name";
    internal const string ImageJob = "Build Docker images";
    internal const string WorkflowName = "Benchmarks";
    internal const string InProgress = "in_progress";
    internal const string JobEnvironment = "KEYLOAD_COMPARISON_JOB_ID=";
    internal const string Failure = "The actual GitHub current-job capture child failed its bound.";

    internal static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException(Failure);

    internal static string JobFile() => Path.Combine(Required(RunnerTemp), Capture, Job);
}
