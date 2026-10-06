using System.Globalization;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteUnsupportedProducerFixture
{
    private const string IncompatibleRevision = "c16a1d928d7d6941db74403e47f3dbcea206d86a";
    private const int SingleAttempt = 1;
    private const int CandidateRunCount = 2;
    private const long NextIdentity = 1;
    private const string RunUrl = "https://github.com/managedcode/KeyLoad/actions/runs/";
    private const string Completed = "completed";

    internal static async Task<(SiteUnsupportedProducerCandidate Older, SiteUnsupportedProducerCandidate Current)> InstallIncompatibleCandidateAsync(
        SiteIsolatedGitHubScope scope, CancellationToken token)
    {
        var runsPath = Path.Combine(scope.Capture, SiteIsolatedGitHubFields.MetadataDirectory,
            SiteIsolatedGitHubFields.Runs);
        var current = await ReadCurrentRunAsync(scope, runsPath, token);
        var pages = await ReadArrayAsync(runsPath, token);
        var existing = pages.SelectMany(page => page![SiteIsolatedGitHubFields.WorkflowRuns]!.AsArray())
            .Select(item => item!.AsObject()).ToArray();
        var unsupported = CreateUnsupportedRun(current, existing);
        var replacement = new JsonObject
        {
            [SiteIsolatedGitHubFields.TotalCount] = CandidateRunCount,
            [SiteIsolatedGitHubFields.WorkflowRuns] = new JsonArray(unsupported, current.DeepClone()),
        };
        await File.WriteAllTextAsync(runsPath, new JsonArray(replacement).ToJsonString(), token);
        return (CreateCandidate(unsupported), CreateCandidate(current));
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

    private static JsonObject CreateUnsupportedRun(JsonObject current, JsonObject[] existing)
    {
        var run = (JsonObject)current.DeepClone();
        var id = existing.Max(item => item[SiteIsolatedGitHubTokens.Id]!.GetValue<long>()) + NextIdentity;
        var number = existing.Max(item => item[SiteIsolatedGitHubFields.NativeRunNumber]!.GetValue<long>()) + NextIdentity;
        run[SiteIsolatedGitHubTokens.Id] = id;
        run[SiteIsolatedGitHubFields.NativeRunNumber] = number;
        run[SiteIsolatedGitHubFields.NativeAttempt] = SingleAttempt;
        run[SiteIsolatedGitHubFields.NativeHeadSha] = IncompatibleRevision;
        run[SiteIsolatedGitHubFields.NativeHtmlUrl] = RunUrl + id.ToString(CultureInfo.InvariantCulture);
        run[SiteIsolatedGitHubFields.Status] = Completed;
        run[SiteIsolatedGitHubTokens.Conclusion] = SiteIsolatedGitHubFields.Failure;
        return run;
    }

    private static SiteUnsupportedProducerCandidate CreateCandidate(JsonObject run) => new(
        run[SiteIsolatedGitHubTokens.Id]!.GetValue<long>(),
        run[SiteIsolatedGitHubFields.NativeAttempt]!.GetValue<int>(),
        run[SiteIsolatedGitHubFields.NativeHeadSha]!.GetValue<string>(),
        run[SiteIsolatedGitHubFields.Event]!.GetValue<string>(),
        run[SiteIsolatedGitHubTokens.Conclusion]!.GetValue<string>());

    private static async Task<JsonArray> ReadArrayAsync(string path, CancellationToken token) =>
        JsonNode.Parse(await File.ReadAllTextAsync(path, token))!.AsArray();
}
