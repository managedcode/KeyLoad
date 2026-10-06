using System.Globalization;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

[NotInParallel(SiteIsolatedGitHubFields.NativeFixtureKey)]
internal sealed class SiteBenchmarkProducerSelectionTests
{
    private const string MetadataCaptureDirectory = "instrumented-current-publish";
    private const string OptionalArgument = "--optional=true";
    private const string BenchmarksWorkflowPath = ".github/workflows/benchmarks.yml";
    private const string SelectionAttempt = SiteIsolatedGitHubTokens.Attempt;

    [Test]
    public async Task AcPipe003CapturesTheAuthenticCurrentBenchmarkRunThroughInstrumentedProductionContext()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var site = SiteTestInputs.Read();
        var siteRevision = RequiredRevisionEnvironment(SitePublicationTokens.SourceRevisionEnvironment);
        var workflowRevision = RequiredRevisionEnvironment(SitePublicationTokens.ControlRevisionEnvironment);
        var accepted = await SiteIsolatedGitHubInputs.ReadAsync(token);
        var sourceRevision = accepted.Metadata[SiteIsolatedGitHubTokens.Source]!
            [SiteIsolatedGitHubTokens.Measured]!.GetValue<string>();
        var runId = accepted.Metadata[SiteIsolatedGitHubTokens.Run]![SiteIsolatedGitHubTokens.Id]!.GetValue<long>();
        var attempt = accepted.Metadata[SiteIsolatedGitHubTokens.Run]![SiteIsolatedGitHubTokens.Attempt]!.GetValue<int>();

        await using var temporary = SiteTempDirectory.Create();
        var capture = Path.Combine(temporary.Path, MetadataCaptureDirectory);
        var start = SiteIsolatedGitHubNodeProcess.CreateStart(site.Repository);
        start.ArgumentList.Add(Path.Combine(site.Repository, SiteIsolatedGitHubTokens.Module));
        start.ArgumentList.Add(SiteIsolatedGitHubFields.CaptureMetadata);
        start.ArgumentList.Add(SiteIsolatedGitHubFields.InputArgument + capture);
        start.ArgumentList.Add(SiteIsolatedGitHubFields.ModeArgument + SiteIsolatedGitHubTokens.Publish);
        start.ArgumentList.Add(SiteIsolatedGitHubFields.SiteArgument + siteRevision);
        start.ArgumentList.Add(SiteIsolatedGitHubFields.ControlArgument + workflowRevision);
        start.ArgumentList.Add(OptionalArgument);
        var process = await SiteIsolatedGitHubNativeProcess.RunAsync(start, token);

        await Assert.That(process.ExitCode).IsEqualTo(SiteIsolatedGitHubTokens.Zero);
        await Assert.That(process.StandardError.Length).IsEqualTo(SiteIsolatedGitHubTokens.Zero);
        using var document = JsonDocument.Parse(process.StandardOutput);
        await Assert.That(document.RootElement.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        var proof = document.RootElement.GetProperty(SiteIsolatedGitHubFields.Result);
        await Assert.That(proof.GetProperty(SiteIsolatedGitHubTokens.Mode).GetString())
            .IsEqualTo(SiteIsolatedGitHubTokens.Publish);
        await Assert.That(proof.GetProperty(SiteIsolatedGitHubTokens.Run)
            .GetProperty(SiteIsolatedGitHubTokens.Id).GetInt64()).IsEqualTo(runId);
        await Assert.That(proof.GetProperty(SiteIsolatedGitHubTokens.Run)
            .GetProperty(SiteIsolatedGitHubTokens.Attempt).GetInt32()).IsEqualTo(attempt);
        await Assert.That(proof.GetProperty(SiteIsolatedGitHubTokens.Source)
            .GetProperty(SiteIsolatedGitHubTokens.Measured).GetString()).IsEqualTo(sourceRevision);
        await Assert.That(proof.GetProperty(SiteIsolatedGitHubFields.WorkflowKey)
            .GetProperty(SiteIsolatedGitHubTokens.Path).GetString()).IsEqualTo(BenchmarksWorkflowPath);
    }

    [Test]
    public async Task AcPipe002SelectsOnlyTheAuthenticatedCurrentBenchmarkProducerTuple()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteIsolatedGitHubScope.CreateAsync(token);
        var run = scope.Inputs.Metadata[SiteIsolatedGitHubTokens.Run]!;
        var cohort = scope.Inputs.Metadata[SiteIsolatedGitHubTokens.Cohort]!;
        var runId = run[SiteIsolatedGitHubTokens.Id]!.GetValue<long>();
        var attempt = run[SiteIsolatedGitHubTokens.Attempt]!.GetValue<int>();
        var sourceRevision = cohort[SiteIsolatedGitHubTokens.SourceRevision]!.GetValue<string>();
        var (producerEvent, conclusion) = await ReadProducerStateAsync(scope, token);
        var producer = new { runId, attempt, sourceRevision, @event = producerEvent, conclusion };

        var selection = await SelectAsync(scope, SiteIsolatedGitHubTokens.Publish, null,
            producer, token);
        await Assert.That(selection.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        var selected = selection.GetProperty(SiteIsolatedGitHubFields.Result);
        await Assert.That(selected.GetProperty(SiteIsolatedGitHubFields.State).GetString())
            .IsEqualTo(SiteIsolatedGitHubFields.Selected);
        await Assert.That(selected.GetProperty(SiteIsolatedGitHubSelectionFields.RunId).GetInt64()).IsEqualTo(runId);
        await Assert.That(selected.GetProperty(SelectionAttempt).GetInt32()).IsEqualTo(attempt);

        var proofEnvelope = await ProveCurrentProducerAsync(scope, producer, token);
        await Assert.That(proofEnvelope.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        var proof = proofEnvelope.GetProperty(SiteIsolatedGitHubFields.Result);
        await Assert.That(proof.GetProperty(SiteIsolatedGitHubTokens.Run)
            .GetProperty(SiteIsolatedGitHubTokens.Id).GetInt64()).IsEqualTo(runId);
        await Assert.That(proof.GetProperty(SiteIsolatedGitHubTokens.Run)
            .GetProperty(SiteIsolatedGitHubTokens.Attempt).GetInt32()).IsEqualTo(attempt);
    }

    [Test]
    public async Task AcPipe002RejectsProducerFallbackToOtherRunAttemptRevisionOrRequestedHistory()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteIsolatedGitHubScope.CreateAsync(token);
        var run = scope.Inputs.Metadata[SiteIsolatedGitHubTokens.Run]!;
        var cohort = scope.Inputs.Metadata[SiteIsolatedGitHubTokens.Cohort]!;
        var runId = run[SiteIsolatedGitHubTokens.Id]!.GetValue<long>();
        var attempt = run[SiteIsolatedGitHubTokens.Attempt]!.GetValue<int>();
        var sourceRevision = cohort[SiteIsolatedGitHubTokens.SourceRevision]!.GetValue<string>();
        var (producerEvent, conclusion) = await ReadProducerStateAsync(scope, token);
        var tokenTuple = new { runId, attempt, sourceRevision, @event = producerEvent, conclusion };

        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish, null,
            new { runId = runId + 1, attempt, sourceRevision, @event = producerEvent, conclusion }, token);
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish, null,
            new { runId, attempt = attempt + 1, sourceRevision, @event = producerEvent, conclusion }, token);
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish, null,
            new { runId, attempt, sourceRevision = SiteIsolatedGitHubTokens.WrongSha, @event = producerEvent, conclusion }, token);
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish, null,
            new { runId, attempt, sourceRevision, @event = SiteIsolatedGitHubFields.WorkflowRun, conclusion }, token);
        var otherEvent = producerEvent == "push" ? "workflow_dispatch" : "push";
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish, null,
            new { runId, attempt, sourceRevision, @event = otherEvent, conclusion }, token);
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish, null,
            new { runId, attempt, sourceRevision, @event = producerEvent, conclusion = SiteIsolatedGitHubFields.Cancelled }, token);
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish,
            runId.ToString(CultureInfo.InvariantCulture), tokenTuple, token);
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Validate, null, tokenTuple, token);
    }

    private static Task<JsonElement> ProveCurrentProducerAsync(SiteIsolatedGitHubScope scope, object producer,
        CancellationToken token)
    {
        var source = scope.Inputs.Metadata[SiteIsolatedGitHubTokens.Source]!;
        var arguments = new
        {
            input = scope.Capture,
            mode = SiteIsolatedGitHubTokens.Publish,
            requestedRun = (string?)null,
            producer,
            // Only parser revalidation of the original integrated archive admits its retained in-progress metadata.
            legacyArchive = true,
            source = new
            {
                website = source[SiteIsolatedGitHubTokens.Website]!.GetValue<string>(),
                control = source[SiteIsolatedGitHubTokens.Control]!.GetValue<string>(),
            },
        };
        return SiteIsolatedGitHubScope.RunAsync(SiteIsolatedGitHubFields.ProofOperation, arguments, token);
    }

    private static async Task<(string Event, string? Conclusion)> ReadProducerStateAsync(
        SiteIsolatedGitHubScope scope, CancellationToken token)
    {
        var path = Path.Combine(Path.GetDirectoryName(scope.SelectedJobsPath)!, SiteIsolatedGitHubFields.NativeRun);
        using var run = JsonDocument.Parse(await File.ReadAllBytesAsync(path, token));
        return (run.RootElement.GetProperty(SiteIsolatedGitHubFields.Event).GetString()!, run.RootElement.GetProperty(SiteIsolatedGitHubTokens.Conclusion).GetString());
    }

    private static string RequiredRevisionEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return SiteCoverageSourceManifestWriter.IsRevision(value)
            ? value!
            : throw new InvalidOperationException(SiteTokens.SiteTestsMissingEnvironment);
    }

    private static Task<JsonElement> SelectAsync(SiteIsolatedGitHubScope scope, string mode,
        string? requestedRun, object? producer, CancellationToken token) => SiteIsolatedGitHubScope.RunAsync(
        SiteIsolatedGitHubFields.SelectionOperation,
        new { input = scope.Capture, mode, requestedRun, producer, legacyArchive = true }, token);

    private static async Task AssertRejectedAsync(SiteIsolatedGitHubScope scope, string mode,
        string? requestedRun, object? producer, CancellationToken token)
    {
        var result = await SelectAsync(scope, mode, requestedRun, producer, token);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();
    }
}
