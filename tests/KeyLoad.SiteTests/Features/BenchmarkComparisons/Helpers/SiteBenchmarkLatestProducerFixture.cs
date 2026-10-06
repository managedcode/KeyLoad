using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBenchmarkLatestProducerFixture
{
    public static async Task PrepareAsync(SiteIsolatedGitHubScope scope, string scenario, CancellationToken token)
    {
        var runPath = Path.Combine(Path.GetDirectoryName(scope.SelectedJobsPath)!, SiteIsolatedGitHubFields.NativeRun);
        var run = JsonNode.Parse(await File.ReadAllBytesAsync(runPath, token))!.AsObject();
        // Controlled API response copies exercise selection only; they never replace original receipts or authenticate GitHub.
        run[SiteIsolatedGitHubFields.Status] = scenario == "noCompleted" ? "in_progress" : "completed";
        run[SiteIsolatedGitHubTokens.Conclusion] = scenario == "noCompleted" ? null : "failure";
        await File.WriteAllTextAsync(runPath, run.ToJsonString(), token);
        var runs = new JsonArray(run.DeepClone());
        if (scenario != "noCompleted")
        {
            var other = OtherRun(run, scenario);
            runs.Add(other);
            if (scenario is "failedAggregate" or "missingAggregate")
            {
                await WriteRejectedAggregateAsync(scope, other, scenario, token);
            }
        }

        var pages = new JsonArray(new JsonObject { [SiteIsolatedGitHubFields.TotalCount] = runs.Count, [SiteIsolatedGitHubFields.WorkflowRuns] = runs });
        await File.WriteAllTextAsync(Path.Combine(scope.Capture, "metadata", "workflow_runs-pages.json"), pages.ToJsonString(), token);
    }

    private static JsonObject OtherRun(JsonObject run, string scenario)
    {
        var other = run.DeepClone().AsObject();
        var id = checked(run[SiteIsolatedGitHubTokens.Id]!.GetValue<long>() + 1);
        other[SiteIsolatedGitHubTokens.Id] = id;
        other[SiteIsolatedGitHubFields.NativeRunNumber] = checked(run[SiteIsolatedGitHubFields.NativeRunNumber]!.GetValue<long>() + (scenario == "olderOtherEvent" ? -1 : 1));
        other[SiteIsolatedGitHubFields.NativeHtmlUrl] = $"https://github.com/managedcode/KeyLoad/actions/runs/{id}";
        other[SiteIsolatedGitHubFields.Event] = run[SiteIsolatedGitHubFields.Event]!.GetValue<string>() == "push" ? "workflow_dispatch" : "push";
        if (scenario == "newerPending")
        {
            other[SiteIsolatedGitHubFields.Status] = "in_progress";
            other[SiteIsolatedGitHubTokens.Conclusion] = null;
        }
        else if (scenario == "cancelledLatest")
        {
            other[SiteIsolatedGitHubTokens.Conclusion] = SiteIsolatedGitHubFields.Cancelled;
        }

        return other;
    }

    private static async Task WriteRejectedAggregateAsync(SiteIsolatedGitHubScope scope, JsonObject run,
        string scenario, CancellationToken token)
    {
        var id = run[SiteIsolatedGitHubTokens.Id]!.GetValue<long>();
        var root = Path.Combine(scope.Capture, "metadata", "attempts", id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            run[SiteIsolatedGitHubFields.NativeAttempt]!.GetValue<int>().ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "run-attempt.json"), run.ToJsonString(), token);
        var source = JsonNode.Parse(await File.ReadAllBytesAsync(scope.SelectedJobsPath, token))!.AsArray();
        var jobId = scope.Inputs.Metadata[SiteIsolatedGitHubTokens.AggregateJob]![SiteIsolatedGitHubTokens.Id]!.GetValue<long>();
        var job = source.SelectMany(page => page![SiteIsolatedGitHubFields.NativeJobs]!.AsArray()).Single(item => item![SiteIsolatedGitHubTokens.Id]!.GetValue<long>() == jobId)!.DeepClone();
        job[SiteIsolatedGitHubFields.NativeRunId] = id;
        job[SiteIsolatedGitHubFields.NativeHtmlUrl] = $"https://github.com/managedcode/KeyLoad/actions/runs/{id}/job/{jobId}";
        job[SiteIsolatedGitHubTokens.Conclusion] = "failure";
        var jobs = scenario == "missingAggregate" ? new JsonArray() : new JsonArray(job);
        var pages = new JsonArray(new JsonObject { [SiteIsolatedGitHubFields.TotalCount] = jobs.Count, [SiteIsolatedGitHubFields.NativeJobs] = jobs });
        await File.WriteAllTextAsync(Path.Combine(root, "jobs-pages.json"), pages.ToJsonString(), token);
    }
}
