using System.Globalization;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

[NotInParallel(SiteIsolatedGitHubFields.NativeFixtureKey)]
internal sealed class SiteBenchmarkProducerSelectionTests
{
    private const string MetadataCaptureDirectory = "instrumented-current-publish";
    private const string SourceRevisionEnvironment = "GITHUB_SHA";
    private const string RunIdEnvironment = "GITHUB_RUN_ID";
    private const string RunAttemptEnvironment = "GITHUB_RUN_ATTEMPT";
    private const string BenchmarksWorkflowPath = ".github/workflows/benchmarks.yml";

    [Test]
    public async Task AcPipe003CapturesTheAuthenticCurrentBenchmarkRunThroughInstrumentedProductionContext()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var site = SiteTestInputs.Read();
        var siteRevision = SiteGitHubEvidenceInputs.RequiredRevisionEnvironment(
            SiteGitHubEvidenceTokens.SiteRevisionEnvironment);
        var workflowRevision = SiteGitHubEvidenceInputs.RequiredRevisionEnvironment(
            SiteGitHubEvidenceTokens.WorkflowRevisionEnvironment);
        var sourceRevision = SiteGitHubEvidenceInputs.RequiredRevisionEnvironment(SourceRevisionEnvironment);
        var runId = long.Parse(Environment.GetEnvironmentVariable(RunIdEnvironment)!, NumberStyles.None,
            CultureInfo.InvariantCulture);
        var attempt = int.Parse(Environment.GetEnvironmentVariable(RunAttemptEnvironment)!, NumberStyles.None,
            CultureInfo.InvariantCulture);

        await using var temporary = SiteTempDirectory.Create();
        var capture = Path.Combine(temporary.Path, MetadataCaptureDirectory);
        var start = SiteIsolatedGitHubNodeProcess.CreateStart(site.Repository);
        start.ArgumentList.Add(Path.Combine(site.Repository, SiteIsolatedGitHubTokens.Module));
        start.ArgumentList.Add(SiteIsolatedGitHubFields.CaptureMetadata);
        start.ArgumentList.Add(SiteIsolatedGitHubFields.InputArgument + capture);
        start.ArgumentList.Add(SiteIsolatedGitHubFields.ModeArgument + SiteIsolatedGitHubTokens.Publish);
        start.ArgumentList.Add(SiteIsolatedGitHubFields.SiteArgument + siteRevision);
        start.ArgumentList.Add(SiteIsolatedGitHubFields.ControlArgument + workflowRevision);
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
        await Assert.That(proof.GetProperty(SiteIsolatedGitHubTokens.Workflow)
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

        var selected = await SelectAsync(scope, SiteIsolatedGitHubTokens.Publish, null,
            new { runId, attempt, sourceRevision }, token);
        await Assert.That(selected.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        var evidence = selected.GetProperty(SiteIsolatedGitHubFields.Result);
        await Assert.That(evidence.GetProperty(SiteIsolatedGitHubTokens.State).GetString())
            .IsEqualTo(SiteIsolatedGitHubTokens.Selected);
        await Assert.That(evidence.GetProperty(SiteIsolatedGitHubTokens.RunId).GetInt64()).IsEqualTo(runId);
        await Assert.That(evidence.GetProperty(SiteIsolatedGitHubTokens.Attempt).GetInt32()).IsEqualTo(attempt);
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
        var tokenTuple = new { runId, attempt, sourceRevision };

        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish, null,
            new { runId = runId + 1, attempt, sourceRevision }, token);
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish, null,
            new { runId, attempt = attempt + 1, sourceRevision }, token);
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish, null,
            new { runId, attempt, sourceRevision = SiteIsolatedGitHubTokens.WrongSha }, token);
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish, runId.ToString(), tokenTuple, token);
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Validate, null, tokenTuple, token);
    }

    private static Task<JsonElement> SelectAsync(SiteIsolatedGitHubScope scope, string mode,
        string? requestedRun, object? producer, CancellationToken token) => SiteIsolatedGitHubScope.RunAsync(
        SiteIsolatedGitHubFields.SelectionOperation, new { input = scope.Capture, mode, requestedRun, producer }, token);

    private static async Task AssertRejectedAsync(SiteIsolatedGitHubScope scope, string mode,
        string? requestedRun, object? producer, CancellationToken token)
    {
        var result = await SelectAsync(scope, mode, requestedRun, producer, token);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();
    }
}
