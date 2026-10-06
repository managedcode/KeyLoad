using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteUnsupportedProducerControls
{
    private const long InvalidRepositoryIdentity = 0;
    private const string UnknownGenerationRevision = "d16a1d928d7d6941db74403e47f3dbcea206d86a";
    private const string UnknownOwnedStep = "Check an unknown benchmark inventory";
    private const string Completed = "completed";
    private static readonly string[] CurrentOwnedSteps =
    [
        "Verify benchmark plan",
        "Download benchmark results",
        "Check control and complete scale accounting",
        "Save combined benchmark results",
        "Save internal scaled cohort receipt",
        "Save GitHub result verification",
    ];

    internal static Task<JsonElement> SelectAsync(SiteIsolatedGitHubScope scope,
        SiteUnsupportedProducerCandidate producer, CancellationToken token) => SelectAsync(scope, producer, true, token);

    internal static Task<JsonElement> SelectAsync(SiteIsolatedGitHubScope scope,
        SiteUnsupportedProducerCandidate producer, bool optional, CancellationToken token)
        => SiteIsolatedGitHubScope.RunAsync(SiteIsolatedGitHubFields.SelectionOperation, new
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

    internal static Task RejectMalformedNativeStepAsync(SiteIsolatedGitHubScope scope,
        SiteUnsupportedProducerCandidate producer, SiteUnsupportedProducerCandidate current, CancellationToken token)
        => RejectJobsMutationAsync(scope, producer, current, pages =>
        {
            var step = pages[0]![SiteIsolatedGitHubFields.NativeJobs]![0]![SiteIsolatedGitHubFields.Steps]![0]!;
            step[SiteIsolatedGitHubTokens.Conclusion] = SiteIsolatedGitHubFields.Failure;
        }, token);

    internal static Task RejectUnauthenticatedCandidateAsync(SiteIsolatedGitHubScope scope,
        SiteUnsupportedProducerCandidate producer, SiteUnsupportedProducerCandidate current, CancellationToken token)
        => SiteUnsupportedProducerAssertions.RejectThenRestoreAndProveCurrentAsync(scope, producer, current,
            [new(OlderRunPath(scope, producer), run =>
                run[SiteIsolatedGitHubFields.NativeHeadRepository]![SiteIsolatedGitHubTokens.Id] = InvalidRepositoryIdentity)], token);

    internal static Task RejectUnknownSourceGenerationAsync(SiteIsolatedGitHubScope scope,
        SiteUnsupportedProducerCandidate producer, SiteUnsupportedProducerCandidate current, CancellationToken token)
    {
        var changedProducer = producer with { SourceRevision = UnknownGenerationRevision };
        return SiteUnsupportedProducerAssertions.RejectThenRestoreAndProveCurrentAsync(scope, changedProducer, current,
        [
            new(RunsPath(scope), pages => FindWorkflowRun(pages, producer.RunId)[SiteIsolatedGitHubFields.NativeHeadSha] = UnknownGenerationRevision),
            new(OlderRunPath(scope, producer), run => run[SiteIsolatedGitHubFields.NativeHeadSha] = UnknownGenerationRevision),
            new(OlderJobsPath(scope, producer), pages => FindAggregateJob(pages, producer.RunId)
                [SiteIsolatedGitHubFields.NativeHeadSha] = UnknownGenerationRevision),
        ], token);
    }

    internal static Task RejectUnknownOwnedStepAsync(SiteIsolatedGitHubScope scope,
        SiteUnsupportedProducerCandidate producer, SiteUnsupportedProducerCandidate current, CancellationToken token)
        => RejectJobsMutationAsync(scope, producer, current, pages =>
        {
            var step = pages[0]![SiteIsolatedGitHubFields.NativeJobs]![0]![SiteIsolatedGitHubFields.Steps]![4]!;
            step[SiteIsolatedGitHubFields.Name] = UnknownOwnedStep;
        }, token);

    internal static Task RejectMisorderedOwnedStepsAsync(SiteIsolatedGitHubScope scope,
        SiteUnsupportedProducerCandidate producer, SiteUnsupportedProducerCandidate current, CancellationToken token)
        => RejectJobsMutationAsync(scope, producer, current, pages =>
        {
            var steps = pages[0]![SiteIsolatedGitHubFields.NativeJobs]![0]![SiteIsolatedGitHubFields.Steps]!.AsArray();
            var first = steps[2]![SiteIsolatedGitHubFields.Name]!.GetValue<string>();
            var second = steps[3]![SiteIsolatedGitHubFields.Name]!.GetValue<string>();
            steps[2]![SiteIsolatedGitHubFields.Name] = second;
            steps[3]![SiteIsolatedGitHubFields.Name] = first;
        }, token);

    internal static Task RejectCurrentInventoryForUnavailableSourceAsync(SiteIsolatedGitHubScope scope,
        SiteUnsupportedProducerCandidate producer, SiteUnsupportedProducerCandidate current, CancellationToken token)
        => RejectJobsMutationAsync(scope, producer, current, pages =>
        {
            var nativeSteps = new[] { "Set up job", "Download source code" }
                .Concat(CurrentOwnedSteps)
                .Concat(new[] { "Post Download source code", "Complete job" });
            var steps = new JsonArray();
            var index = 0;
            foreach (var name in nativeSteps)
            {
                steps.Add(new JsonObject
                {
                    [SiteIsolatedGitHubTokens.Number] = ++index,
                    [SiteIsolatedGitHubFields.Name] = name,
                    [SiteIsolatedGitHubFields.Status] = Completed,
                    [SiteIsolatedGitHubTokens.Conclusion] = SiteIsolatedGitHubTokens.Success,
                });
            }

            pages[0]![SiteIsolatedGitHubFields.NativeJobs]![0]![SiteIsolatedGitHubFields.Steps] = steps;
        }, token);

    private static Task RejectJobsMutationAsync(SiteIsolatedGitHubScope scope,
        SiteUnsupportedProducerCandidate producer, SiteUnsupportedProducerCandidate current, Action<JsonNode> mutate,
        CancellationToken token)
        => SiteUnsupportedProducerAssertions.RejectThenRestoreAndProveCurrentAsync(scope, producer, current,
            [new(OlderJobsPath(scope, producer), mutate)], token);

    private static JsonObject FindWorkflowRun(JsonNode pages, long runId)
        => pages.AsArray().SelectMany(page => page![SiteIsolatedGitHubFields.WorkflowRuns]!.AsArray())
            .Select(item => item!.AsObject())
            .Single(item => item[SiteIsolatedGitHubTokens.Id]!.GetValue<long>() == runId);

    private static JsonObject FindAggregateJob(JsonNode pages, long runId)
        => pages.AsArray().SelectMany(page => page![SiteIsolatedGitHubFields.NativeJobs]!.AsArray())
            .Select(item => item!.AsObject())
            .Single(item => item[SiteIsolatedGitHubFields.NativeRunId]!.GetValue<long>() == runId);

    private static string RunsPath(SiteIsolatedGitHubScope scope)
        => Path.Combine(scope.Capture, SiteIsolatedGitHubFields.MetadataDirectory, SiteIsolatedGitHubFields.Runs);

    private static string OlderJobsPath(SiteIsolatedGitHubScope scope, SiteUnsupportedProducerCandidate producer)
        => Path.Combine(OlderAttemptDirectory(scope, producer), SiteIsolatedGitHubFields.Jobs);

    private static string OlderRunPath(SiteIsolatedGitHubScope scope, SiteUnsupportedProducerCandidate producer)
        => Path.Combine(OlderAttemptDirectory(scope, producer), SiteIsolatedGitHubFields.NativeRun);

    private static string OlderAttemptDirectory(SiteIsolatedGitHubScope scope, SiteUnsupportedProducerCandidate producer)
        => Path.Combine(scope.Capture, SiteIsolatedGitHubFields.MetadataDirectory, SiteIsolatedGitHubFields.Attempts,
            producer.RunId.ToString(CultureInfo.InvariantCulture), producer.Attempt.ToString(CultureInfo.InvariantCulture));
}
