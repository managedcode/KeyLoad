using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteUnsupportedProducerFixture
{
    private const string IncompatibleRevision = "c16a1d928d7d6941db74403e47f3dbcea206d86a";
    private const string AggregateJobName = "Combine benchmark results";
    private const string VerifyPlan = "Verify benchmark plan";
    private const string DownloadResults = "Download benchmark results";
    private const string LegacyCheck = "Check all 270 benchmark results";
    private const string LegacyGenerate = "Generate website benchmark data";
    private const string LegacySave = "Save website benchmark data";
    private const string SaveCombined = "Save combined benchmark results";
    private const string SaveVerification = "Save GitHub result verification";
    private const string RunUrl = "https://github.com/managedcode/KeyLoad/actions/runs/";
    private const string JobUrl = "/job/";
    private const string Completed = "completed";
    private const string HeadSha = SiteIsolatedGitHubFields.NativeHeadSha;
    private const int SingleAttempt = 1;
    private const int FirstSequenceNumber = 1;
    private const int CandidateRunCount = 2;
    private const int SingleAggregateJob = 1;
    private const long NextIdentity = 1;
    private const long InvalidRepositoryIdentity = 0;

    private static readonly string[] LegacySteps =
    [
        VerifyPlan,
        DownloadResults,
        LegacyCheck,
        LegacyGenerate,
        LegacySave,
        SaveCombined,
        SaveVerification,
    ];

    internal static async Task<(SiteUnsupportedProducerCandidate Older, SiteUnsupportedProducerCandidate Current)> InstallIncompatibleCandidateAsync(
        SiteIsolatedGitHubScope scope, CancellationToken token)
    {
        var runsPath = Path.Combine(scope.Capture, SiteIsolatedGitHubFields.MetadataDirectory,
            SiteIsolatedGitHubFields.Runs);
        var currentRun = await ReadCurrentRunAsync(scope, runsPath, token);
        var runPages = await ReadArrayAsync(runsPath, token);
        var existing = runPages.SelectMany(page => page![SiteIsolatedGitHubFields.WorkflowRuns]!.AsArray())
            .Select(item => item!.AsObject()).ToArray();
        var oldRun = CreateOlderRun(currentRun, existing);
        await WriteRunPagesAsync(runsPath, oldRun, currentRun, token);
        var currentProducer = CreateProducer(currentRun);
        var olderProducer = CreateProducer(oldRun);
        await WriteOlderAttemptAsync(scope, oldRun, token);
        return (olderProducer, currentProducer);
    }

    private static async Task<JsonObject> ReadCurrentRunAsync(SiteIsolatedGitHubScope scope, string runsPath,
        CancellationToken token)
    {
        var pages = await ReadArrayAsync(runsPath, token);
        var selectedId = scope.Inputs.Metadata[SiteIsolatedGitHubTokens.Run]!
            [SiteIsolatedGitHubTokens.Id]!.GetValue<long>();
        return pages.SelectMany(page => page![SiteIsolatedGitHubFields.WorkflowRuns]!.AsArray())
            .Select(item => item!.AsObject())
            .Single(item => item[SiteIsolatedGitHubTokens.Id]!.GetValue<long>() == selectedId);
    }

    private static JsonObject CreateOlderRun(JsonObject current, JsonObject[] existing)
    {
        var run = (JsonObject)current.DeepClone();
        var id = existing.Max(item => item[SiteIsolatedGitHubTokens.Id]!.GetValue<long>()) + NextIdentity;
        var number = existing.Max(item => item[SiteIsolatedGitHubFields.NativeRunNumber]!.GetValue<long>()) + NextIdentity;
        run[SiteIsolatedGitHubTokens.Id] = id;
        run[SiteIsolatedGitHubFields.NativeRunNumber] = number;
        run[SiteIsolatedGitHubFields.NativeAttempt] = SingleAttempt;
        run[HeadSha] = IncompatibleRevision;
        run[SiteIsolatedGitHubFields.NativeHtmlUrl] = RunUrl + id.ToString(CultureInfo.InvariantCulture);
        run[SiteIsolatedGitHubFields.Status] = Completed;
        run[SiteIsolatedGitHubTokens.Conclusion] = SiteIsolatedGitHubTokens.Success;
        return run;
    }

    private static async Task WriteRunPagesAsync(string path, JsonObject older, JsonObject current,
        CancellationToken token)
    {
        var page = new JsonObject
        {
            [SiteIsolatedGitHubFields.TotalCount] = CandidateRunCount,
            [SiteIsolatedGitHubFields.WorkflowRuns] = new JsonArray(older, current.DeepClone()),
        };
        await File.WriteAllTextAsync(path, new JsonArray(page).ToJsonString(), token);
    }

    private static async Task WriteOlderAttemptAsync(SiteIsolatedGitHubScope scope, JsonObject run,
        CancellationToken token)
    {
        var id = run[SiteIsolatedGitHubTokens.Id]!.GetValue<long>();
        var attempt = run[SiteIsolatedGitHubFields.NativeAttempt]!.GetValue<int>();
        var root = Path.Combine(scope.Capture, SiteIsolatedGitHubFields.MetadataDirectory,
            SiteIsolatedGitHubFields.Attempts, id.ToString(CultureInfo.InvariantCulture),
            attempt.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, SiteIsolatedGitHubFields.NativeRun), run.ToJsonString(), token);
        var pages = await ReadArrayAsync(scope.SelectedJobsPath, token);
        var job = pages.SelectMany(page => page![SiteIsolatedGitHubFields.NativeJobs]!.AsArray())
            .Select(item => item!.AsObject()).Single(item => item[SiteIsolatedGitHubFields.Name]!.GetValue<string>() == AggregateJobName);
        var oldJob = CreateOlderJob(job, id, attempt);
        var jobs = new JsonObject
        {
            [SiteIsolatedGitHubFields.TotalCount] = SingleAggregateJob,
            [SiteIsolatedGitHubFields.NativeJobs] = new JsonArray(oldJob),
        };
        await File.WriteAllTextAsync(Path.Combine(root, SiteIsolatedGitHubFields.Jobs),
            new JsonArray(jobs).ToJsonString(), token);
    }

    private static JsonObject CreateOlderJob(JsonObject current, long runId, int attempt)
    {
        var job = (JsonObject)current.DeepClone();
        var jobId = current[SiteIsolatedGitHubTokens.Id]!.GetValue<long>() + NextIdentity;
        job[SiteIsolatedGitHubTokens.Id] = jobId;
        job[SiteIsolatedGitHubFields.NativeRunId] = runId;
        job[SiteIsolatedGitHubFields.NativeAttempt] = attempt;
        job[HeadSha] = IncompatibleRevision;
        job[SiteIsolatedGitHubFields.NativeHtmlUrl] = RunUrl
            + runId.ToString(CultureInfo.InvariantCulture) + JobUrl + jobId.ToString(CultureInfo.InvariantCulture);
        job[SiteIsolatedGitHubFields.Steps] = CreateSuccessfulLegacySteps();
        return job;
    }

    private static JsonArray CreateSuccessfulLegacySteps()
    {
        var steps = new JsonArray();
        for (var index = 0; index < LegacySteps.Length; index++)
        {
            steps.Add(new JsonObject
            {
                [SiteIsolatedGitHubTokens.Number] = index + FirstSequenceNumber,
                [SiteIsolatedGitHubFields.Name] = LegacySteps[index],
                [SiteIsolatedGitHubFields.Status] = Completed,
                [SiteIsolatedGitHubTokens.Conclusion] = SiteIsolatedGitHubTokens.Success,
            });
        }

        return steps;
    }

    private static SiteUnsupportedProducerCandidate CreateProducer(JsonObject run) => new(
        run[SiteIsolatedGitHubTokens.Id]!.GetValue<long>(),
        run[SiteIsolatedGitHubFields.NativeAttempt]!.GetValue<int>(),
        run[HeadSha]!.GetValue<string>(),
        run[SiteIsolatedGitHubFields.Event]!.GetValue<string>(),
        run[SiteIsolatedGitHubTokens.Conclusion]!.GetValue<string>());

    internal static Task<JsonElement> SelectAsync(SiteIsolatedGitHubScope scope, SiteUnsupportedProducerCandidate producer,
        CancellationToken token, bool optional = true) => SiteIsolatedGitHubScope.RunAsync(SiteIsolatedGitHubFields.SelectionOperation,
        new
        {
            input = scope.Capture,
            mode = SiteIsolatedGitHubTokens.Publish,
            requestedRun = (string?)null,
            producer = new
            {
                runId = producer.RunId,
                attempt = producer.Attempt,
                sourceRevision = producer.SourceRevision,
                @event = producer.Event,
                conclusion = producer.Conclusion,
            },
            legacyArchive = false,
            optional,
        }, token);

    private static async Task<JsonArray> ReadArrayAsync(string path, CancellationToken token) =>
        JsonNode.Parse(await File.ReadAllBytesAsync(path, token))!.AsArray();

    internal static async Task RejectMalformedLegacyCandidateAsync(SiteIsolatedGitHubScope scope,
        SiteUnsupportedProducerCandidate producer, CancellationToken token)
    {
        var path = OlderJobsPath(scope, producer);
        var pages = await ReadArrayAsync(path, token);
        var job = pages[0]![SiteIsolatedGitHubFields.NativeJobs]![0]!.AsObject();
        job[SiteIsolatedGitHubFields.Steps]![0]![SiteIsolatedGitHubTokens.Conclusion] = SiteIsolatedGitHubFields.Failure;
        await File.WriteAllTextAsync(path, pages.ToJsonString(), token);
        var response = await SelectAsync(scope, producer, token);
        await Assert.That(response.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();
    }

    internal static async Task RejectUnauthenticatedLegacyCandidateAsync(SiteIsolatedGitHubScope scope,
        SiteUnsupportedProducerCandidate producer, CancellationToken token)
    {
        var path = OlderRunPath(scope, producer);
        var run = JsonNode.Parse(await File.ReadAllBytesAsync(path, token))!.AsObject();
        run[SiteIsolatedGitHubFields.NativeHeadRepository]![SiteIsolatedGitHubTokens.Id] = InvalidRepositoryIdentity;
        await File.WriteAllTextAsync(path, run.ToJsonString(), token);
        var response = await SelectAsync(scope, producer, token);
        await Assert.That(response.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();
    }

    private static string OlderJobsPath(SiteIsolatedGitHubScope scope, SiteUnsupportedProducerCandidate producer) =>
        Path.Combine(OlderAttemptDirectory(scope, producer), SiteIsolatedGitHubFields.Jobs);

    private static string OlderRunPath(SiteIsolatedGitHubScope scope, SiteUnsupportedProducerCandidate producer) =>
        Path.Combine(OlderAttemptDirectory(scope, producer), SiteIsolatedGitHubFields.NativeRun);

    private static string OlderAttemptDirectory(SiteIsolatedGitHubScope scope, SiteUnsupportedProducerCandidate producer) =>
        Path.Combine(scope.Capture, SiteIsolatedGitHubFields.MetadataDirectory, SiteIsolatedGitHubFields.Attempts,
            producer.RunId.ToString(CultureInfo.InvariantCulture), producer.Attempt.ToString(CultureInfo.InvariantCulture));
}
