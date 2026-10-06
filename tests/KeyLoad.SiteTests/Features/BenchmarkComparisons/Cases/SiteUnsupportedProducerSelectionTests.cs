using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

[NotInParallel(SiteIsolatedGitHubFields.NativeFixtureKey)]
internal sealed class SiteUnsupportedProducerSelectionTests
{
    private const string Unavailable = "unavailable";
    private const string OptionalArgument = "--optional=true";
    private const string OptionalCaptureDirectory = "optional-metadata-capture";
    private const string IncompatibleRevision = "c16a1d928d7d6941db74403e47f3dbcea206d86a";
    private const string ProducerKey = "producer";
    private const string JobKey = "job";
    private const string ConclusionKey = "conclusion";
    private const string MetricsKey = "metrics";

    [Test]
    public async Task AcBcWeb002SkipsIncompatibleOldAggregateAndKeepsCurrentProducerSelected()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteIsolatedGitHubScope.CreateAsync(token);
        var candidates = await SiteUnsupportedProducerFixture.InstallIncompatibleCandidateAsync(scope, token);

        var unsupported = await SiteUnsupportedProducerControls.SelectAsync(scope, candidates.Older, token);
        await Assert.That(unsupported.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        var unavailable = unsupported.GetProperty(SiteIsolatedGitHubFields.Result);
        await Assert.That(unavailable.GetProperty(SiteIsolatedGitHubFields.State).GetString()).IsEqualTo(Unavailable);
        await Assert.That(unavailable.GetProperty(ProducerKey).GetProperty(ConclusionKey).GetString())
            .IsEqualTo(SiteIsolatedGitHubFields.Failure);
        await Assert.That(unavailable.TryGetProperty(JobKey, out _)).IsFalse();
        await Assert.That(unavailable.TryGetProperty(MetricsKey, out _)).IsFalse();
        var strict = await SiteUnsupportedProducerControls.SelectAsync(scope, candidates.Older, false, token);
        await Assert.That(strict.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();
        await SiteUnsupportedProducerAssertions.AssertCurrentSelectedAsync(scope, candidates.Current, token);

        await SiteUnsupportedProducerControls.RejectMalformedNativeStepAsync(scope, candidates.Older, candidates.Current, token);
        await SiteUnsupportedProducerControls.RejectUnauthenticatedCandidateAsync(scope, candidates.Older, candidates.Current, token);
        await SiteUnsupportedProducerControls.RejectUnknownOwnedStepAsync(scope, candidates.Older, candidates.Current, token);
        await SiteUnsupportedProducerControls.RejectMisorderedOwnedStepsAsync(scope, candidates.Older, candidates.Current, token);
        await SiteUnsupportedProducerAssertions.AssertCurrentSelectedAsync(scope, candidates.Current, token);
    }

    [Test]
    public async Task AcBcWeb002DoesNotClassifyTheSameInventoryForAnotherSourceAsUnavailable()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteIsolatedGitHubScope.CreateAsync(token);
        var candidates = await SiteUnsupportedProducerFixture.InstallIncompatibleCandidateAsync(scope, token);
        await SiteUnsupportedProducerControls.RejectUnknownSourceGenerationAsync(scope, candidates.Older, candidates.Current, token);
    }

    [Test]
    public async Task AcBcWeb002DoesNotTreatCurrentStepsAsTheUnavailableOlderGeneration()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteIsolatedGitHubScope.CreateAsync(token);
        var candidates = await SiteUnsupportedProducerFixture.InstallIncompatibleCandidateAsync(scope, token);
        await SiteUnsupportedProducerControls.RejectCurrentInventoryForUnavailableSourceAsync(scope, candidates.Older, candidates.Current, token);
    }

    [Test]
    public async Task AcBcWeb002OptionalCaptureSkipsIncompatibleProducerWithoutInventingMetrics()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var site = SiteTestInputs.Read();
        var siteRevision = RequiredRevisionEnvironment(SitePublicationTokens.SourceRevisionEnvironment);
        var workflowRevision = RequiredRevisionEnvironment(SitePublicationTokens.ControlRevisionEnvironment);
        await using var temporary = SiteTempDirectory.Create();
        var capture = Path.Combine(temporary.Path, OptionalCaptureDirectory);
        var start = SiteIsolatedGitHubNodeProcess.CreateStart(site.Repository);
        start.ArgumentList.Add(Path.Combine(site.Repository, SiteIsolatedGitHubTokens.Module));
        start.ArgumentList.Add(SiteIsolatedGitHubFields.CaptureMetadata);
        start.ArgumentList.Add(SiteIsolatedGitHubFields.InputArgument + capture);
        start.ArgumentList.Add(SiteIsolatedGitHubFields.ModeArgument + SiteIsolatedGitHubTokens.Publish);
        start.ArgumentList.Add(OptionalArgument);
        start.ArgumentList.Add(SiteIsolatedGitHubFields.SiteArgument + siteRevision);
        start.ArgumentList.Add(SiteIsolatedGitHubFields.ControlArgument + workflowRevision);
        var process = await SiteIsolatedGitHubNativeProcess.RunAsync(start, token);

        await Assert.That(process.ExitCode).IsEqualTo(SiteIsolatedGitHubTokens.Zero);
        await Assert.That(process.StandardError.Length).IsEqualTo(SiteIsolatedGitHubTokens.Zero);
        using var document = JsonDocument.Parse(process.StandardOutput);
        await Assert.That(document.RootElement.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        var result = document.RootElement.GetProperty(SiteIsolatedGitHubFields.Result);
        var state = result.GetProperty(SiteIsolatedGitHubFields.State).GetString();
        await Assert.That(state == Unavailable || state == SiteIsolatedGitHubTokens.MetadataState).IsTrue();
        if (state == Unavailable)
        {
            await Assert.That(result.GetProperty(ProducerKey).ValueKind)
                .IsEqualTo(JsonValueKind.Null);
        }
        else
        {
            var measured = result.GetProperty(SiteIsolatedGitHubTokens.Source)
                .GetProperty(SiteIsolatedGitHubTokens.Measured).GetString();
            await Assert.That(measured == IncompatibleRevision).IsFalse();
        }

        var receiptPath = Path.Combine(capture, SiteIsolatedGitHubTokens.Metadata);
        await Assert.That(File.Exists(receiptPath)).IsTrue();
        using var receipt = JsonDocument.Parse(await File.ReadAllBytesAsync(receiptPath, token));
        await Assert.That(receipt.RootElement.GetProperty(SiteIsolatedGitHubFields.State).GetString())
            .IsEqualTo(state);
    }

    private static string RequiredRevisionEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return SiteCoverageSourceManifestWriter.IsRevision(value)
            ? value!
            : throw new InvalidOperationException(SiteIsolatedGitHubTokens.Missing);
    }
}
