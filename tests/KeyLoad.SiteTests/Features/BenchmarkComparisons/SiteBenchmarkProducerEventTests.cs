using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteBenchmarkProducerEventTests
{
    [Test]
    [Arguments(SiteIsolatedGitHubFields.Action)]
    [Arguments(SiteIsolatedGitHubFields.RepositoryIdCase)]
    [Arguments(SiteIsolatedGitHubFields.RepositoryNameCase)]
    [Arguments(SiteIsolatedGitHubFields.RunRepositoryIdCase)]
    [Arguments(SiteIsolatedGitHubFields.RunRepositoryNameCase)]
    [Arguments(SiteIsolatedGitHubFields.HeadRepositoryIdCase)]
    [Arguments(SiteIsolatedGitHubFields.HeadRepositoryNameCase)]
    [Arguments(SiteIsolatedGitHubFields.BranchCase)]
    [Arguments(SiteIsolatedGitHubFields.WorkflowNameCase)]
    [Arguments(SiteIsolatedGitHubFields.WorkflowPathCase)]
    [Arguments(SiteIsolatedGitHubFields.Status)]
    [Arguments(SiteIsolatedGitHubFields.Cancelled)]
    [Arguments(SiteIsolatedGitHubFields.ProducerEventCase)]
    [Arguments(SiteIsolatedGitHubTokens.Source)]
    [Arguments(SiteIsolatedGitHubTokens.Run)]
    [Arguments(SiteIsolatedGitHubTokens.Attempt)]
    [Arguments(SiteIsolatedGitHubFields.MissingRunCase)]
    public async Task AC_BC_FAIL_019_RejectsForeignIncompleteCancelledOrMalformedProducerPayload(string corruption)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteIsolatedGitHubScope.CreateAsync(token);
        await using var temporary = SiteTempDirectory.Create();
        var path = Path.Combine(temporary.Path, "controlled-event.json");
        var payload = await SiteBenchmarkProducerEventFixture.InvalidPayloadAsync(scope, corruption, token);
        await File.WriteAllTextAsync(path, payload.ToJsonString(), token);
        var result = await ProbeAsync(path, token);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();
        await Assert.That(Directory.Exists(temporary.Output)).IsFalse();
    }

    [Test]
    [Arguments("missing")]
    [Arguments("empty")]
    [Arguments("malformed")]
    [Arguments("duplicate")]
    [Arguments("oversized")]
    [Arguments("directory")]
    [Arguments("symlink")]
    public async Task AC_BC_FAIL_019_RejectsUnsafeNativeEventFileBeforeCapture(string corruption)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var path = Path.Combine(temporary.Path, "controlled-event.json");
        await SiteBenchmarkProducerEventFixture.WriteInvalidFileAsync(path, corruption, token);
        var result = await ProbeAsync(path, token);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();
        await Assert.That(Directory.Exists(temporary.Output)).IsFalse();
    }

    private static Task<JsonElement> ProbeAsync(string path, CancellationToken token) =>
        SiteIsolatedGitHubScope.RunAsync("producer-event", new { path }, token);
}

internal static class SiteBenchmarkProducerEventFixture
{
    public static async Task<JsonObject> InvalidPayloadAsync(SiteIsolatedGitHubScope scope, string corruption,
        CancellationToken token)
    {
        var path = Path.Combine(Path.GetDirectoryName(scope.SelectedJobsPath)!, SiteIsolatedGitHubFields.NativeRun);
        var run = JsonNode.Parse(await File.ReadAllBytesAsync(path, token))!.AsObject();
        // These deliberately invalid parser payloads never claim to be the native event or authenticate a provider.
        // Original API bytes stay untouched; completion fields permit each independent rejection to be exercised.
        run[SiteIsolatedGitHubFields.Status] = "completed";
        run[SiteIsolatedGitHubTokens.Conclusion] = "failure";
        var payload = new JsonObject
        {
            [SiteIsolatedGitHubFields.Action] = "completed",
            [SiteIsolatedGitHubFields.Repository] = run[SiteIsolatedGitHubFields.Repository]!.DeepClone(),
            [SiteIsolatedGitHubFields.WorkflowRun] = run,
        };
        Mutate(payload, run, corruption);
        return payload;
    }

    private static void Mutate(JsonObject payload, JsonObject run, string corruption)
    {
        var mutations = new Dictionary<string, Action>(StringComparer.Ordinal)
        {
            [SiteIsolatedGitHubFields.Action] = () => payload[SiteIsolatedGitHubFields.Action] = "requested",
            [SiteIsolatedGitHubFields.RepositoryIdCase] = () => payload[SiteIsolatedGitHubFields.Repository]![SiteIsolatedGitHubTokens.Id] = 1,
            [SiteIsolatedGitHubFields.RepositoryNameCase] = () => payload[SiteIsolatedGitHubFields.Repository]![SiteIsolatedGitHubFields.NativeRepositoryName] = "foreign/KeyLoad",
            [SiteIsolatedGitHubFields.RunRepositoryIdCase] = () => run[SiteIsolatedGitHubFields.Repository]![SiteIsolatedGitHubTokens.Id] = 1,
            [SiteIsolatedGitHubFields.RunRepositoryNameCase] = () => run[SiteIsolatedGitHubFields.Repository]![SiteIsolatedGitHubFields.NativeRepositoryName] = "foreign/KeyLoad",
            [SiteIsolatedGitHubFields.HeadRepositoryIdCase] = () => run[SiteIsolatedGitHubFields.NativeHeadRepository]![SiteIsolatedGitHubTokens.Id] = 1,
            [SiteIsolatedGitHubFields.HeadRepositoryNameCase] = () => run[SiteIsolatedGitHubFields.NativeHeadRepository]![SiteIsolatedGitHubFields.NativeRepositoryName] = "foreign/KeyLoad",
            [SiteIsolatedGitHubFields.BranchCase] = () => run[SiteIsolatedGitHubFields.NativeHeadBranch] = "foreign",
            [SiteIsolatedGitHubFields.WorkflowNameCase] = () => run[SiteIsolatedGitHubFields.Name] = "CI",
            [SiteIsolatedGitHubFields.WorkflowPathCase] = () => run[SiteIsolatedGitHubTokens.Path] = ".github/workflows/ci.yml",
            [SiteIsolatedGitHubFields.Status] = () => run[SiteIsolatedGitHubFields.Status] = "in_progress",
            [SiteIsolatedGitHubFields.Cancelled] = () => run[SiteIsolatedGitHubTokens.Conclusion] = SiteIsolatedGitHubFields.Cancelled,
            [SiteIsolatedGitHubFields.ProducerEventCase] = () => run[SiteIsolatedGitHubFields.Event] = SiteIsolatedGitHubFields.WorkflowRun,
            [SiteIsolatedGitHubTokens.Source] = () => run[SiteIsolatedGitHubFields.NativeHeadSha] = "invalid",
            [SiteIsolatedGitHubTokens.Run] = () => run[SiteIsolatedGitHubTokens.Id] = 0,
            [SiteIsolatedGitHubTokens.Attempt] = () => run[SiteIsolatedGitHubFields.NativeAttempt] = 0,
            [SiteIsolatedGitHubFields.MissingRunCase] = () => payload.Remove(SiteIsolatedGitHubFields.WorkflowRun),
        };
        mutations[corruption]();
    }

    public static Task WriteInvalidFileAsync(string path, string corruption, CancellationToken token)
    {
        switch (corruption)
        {
            case "missing":
                return Task.CompletedTask;
            case "directory":
                Directory.CreateDirectory(path);
                return Task.CompletedTask;
            case "symlink":
                File.CreateSymbolicLink(path, Path.Combine(Path.GetDirectoryName(path)!, "missing-target"));
                return Task.CompletedTask;
            default:
                return File.WriteAllTextAsync(path, InvalidText(corruption), token);
        }
    }

    private static string InvalidText(string corruption) => corruption switch
    {
        "empty" => string.Empty,
        "malformed" => "{",
        "duplicate" => "{\"action\":\"completed\",\"action\":\"requested\"}",
        "oversized" => new string(' ', 1_048_577),
        _ => throw new ArgumentOutOfRangeException(nameof(corruption)),
    };
}

internal sealed class SiteBenchmarkLatestProducerTests
{
    [Test]
    [Arguments("newerPending")]
    [Arguments("olderOtherEvent")]
    public async Task AC_BC_FAIL_020_LatestCompletedProducerSpansPushAndManualWhileIgnoringPending(string scenario)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteIsolatedGitHubScope.CreateAsync(token);
        var original = scope.Inputs.Metadata.DeepClone();
        await SiteBenchmarkLatestProducerFixture.PrepareAsync(scope, scenario, token);
        var result = await ProbeAsync(scope, token);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Result)
            .GetProperty(SiteIsolatedGitHubFields.State).GetString()).IsEqualTo(SiteIsolatedGitHubFields.Selected);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Result)
            .GetProperty(SiteIsolatedGitHubSelectionFields.RunId).GetInt64()).IsEqualTo(
                scope.Inputs.Metadata[SiteIsolatedGitHubTokens.Run]![SiteIsolatedGitHubTokens.Id]!.GetValue<long>());
        await Assert.That(JsonNode.DeepEquals(scope.Inputs.Metadata, original)).IsTrue();
    }

    [Test]
    [Arguments("failedAggregate")]
    [Arguments("missingAggregate")]
    [Arguments("cancelledLatest")]
    [Arguments("noCompleted")]
    public async Task AC_BC_FAIL_020_InvalidLatestCompletedProducerCannotFallBackToOlderMeasurements(string scenario)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteIsolatedGitHubScope.CreateAsync(token);
        var original = scope.Inputs.Metadata.DeepClone();
        await SiteBenchmarkLatestProducerFixture.PrepareAsync(scope, scenario, token);
        var result = await ProbeAsync(scope, token);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();
        await Assert.That(JsonNode.DeepEquals(scope.Inputs.Metadata, original)).IsTrue();
    }

    private static Task<JsonElement> ProbeAsync(SiteIsolatedGitHubScope scope, CancellationToken token) =>
        SiteIsolatedGitHubScope.RunAsync("latest-selection", new { input = scope.Capture }, token);
}

internal static class SiteBenchmarkLatestProducerFixture
{
    public static async Task PrepareAsync(SiteIsolatedGitHubScope scope, string scenario, CancellationToken token)
    {
        var runPath = Path.Combine(Path.GetDirectoryName(scope.SelectedJobsPath)!, SiteIsolatedGitHubFields.NativeRun);
        var run = JsonNode.Parse(await File.ReadAllBytesAsync(runPath, token))!.AsObject();
        // Controlled parser copies exercise selection only; they never replace original receipts or authenticate GitHub.
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
