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

    [Test]
    public async Task AcBcWeb002SkipsIncompatibleOldAggregateAndKeepsCurrentProducerSelected()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteIsolatedGitHubScope.CreateAsync(token);
        var candidates = await SiteUnsupportedProducerFixture.InstallIncompatibleCandidateAsync(scope, token);

        var unsupported = await SiteUnsupportedProducerFixture.SelectAsync(scope, candidates.Older, token);
        await Assert.That(unsupported.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        await Assert.That(unsupported.GetProperty(SiteIsolatedGitHubFields.Result)
            .GetProperty(SiteIsolatedGitHubFields.State).GetString()).IsEqualTo(Unavailable);
        var strict = await SiteUnsupportedProducerFixture.SelectAsync(scope, candidates.Older, token, optional: false);
        await Assert.That(strict.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();

        var current = await SiteUnsupportedProducerFixture.SelectAsync(scope, candidates.Current, token);
        await Assert.That(current.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        var result = current.GetProperty(SiteIsolatedGitHubFields.Result);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.State).GetString()).IsEqualTo(SiteIsolatedGitHubFields.Selected);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubSelectionFields.RunId).GetInt64())
            .IsEqualTo(candidates.Current.RunId);

        await SiteUnsupportedProducerFixture.RejectMalformedLegacyCandidateAsync(scope, candidates.Older, token);
        await SiteUnsupportedProducerFixture.RejectUnauthenticatedLegacyCandidateAsync(scope, candidates.Older, token);
        var healthy = await SiteUnsupportedProducerFixture.SelectAsync(scope, candidates.Current, token);
        await Assert.That(healthy.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        await Assert.That(healthy.GetProperty(SiteIsolatedGitHubFields.Result)
            .GetProperty(SiteIsolatedGitHubFields.State).GetString()).IsEqualTo(SiteIsolatedGitHubFields.Selected);
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
