using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteContentCoverageModeTests
{
    [Test]
    public async Task AC_BC_WEB_005_ContentModeUsesSourceOnlyManifestAndAllApplicableCoverageThresholds()
    {
        await Assert.That(SiteCoverageModeSelector.Resolve(null)).IsEqualTo(SiteCoverageMode.Measured);
        await Assert.That(SiteCoverageModeSelector.Resolve(SiteCoverageTokens.MeasuredBenchmarkMode))
            .IsEqualTo(SiteCoverageMode.Measured);
        await Assert.That(SiteCoverageModeSelector.Resolve(SiteCoverageTokens.NoBenchmarkMode))
            .IsEqualTo(SiteCoverageMode.ContentOnly);
        var sources = SiteCoverageModeSelector.Sources(SiteCoverageMode.ContentOnly);
        await Assert.That(sources.Any(path => path.Contains("isolated-", StringComparison.Ordinal) ||
            path.EndsWith("measurements.mjs", StringComparison.Ordinal) ||
            path.EndsWith("measurement-loader.mjs", StringComparison.Ordinal))).IsFalse();

        var repository = Environment.GetEnvironmentVariable(SiteTokens.RepositoryEnvironment);
        var coverageRoot = Environment.GetEnvironmentVariable(SiteCoverageTokens.CoverageRootEnvironment);
        var revision = Environment.GetEnvironmentVariable(SitePublicationTokens.SourceRevisionEnvironment);
        if (repository is null || coverageRoot is null || revision is null)
        {
            throw new InvalidOperationException(SiteCoverageTokens.InvalidRootFailure);
        }

        var manifestPath = Path.Combine(coverageRoot, SiteCoverageTokens.SourceManifestFile);
        var manifest = JsonSerializer.Deserialize<SiteCoverageSourceManifest>(
            await File.ReadAllBytesAsync(manifestPath, TestContext.Current!.Execution.CancellationToken),
            SiteCoverageTokens.JsonOptions);
        await Assert.That(manifest).IsNotNull();
        await Assert.That(manifest!.SourceRevision).IsEqualTo(revision);
        await SiteCoverageSourceManifestWriter.VerifyUnchangedAsync(repository, manifest);
        var currentMode = SiteCoverageModeSelector.Current;
        var selectedSources = SiteCoverageModeSelector.Sources(currentMode);
        var manifestPaths = manifest.Sources.Select(source => source.Path).Order(StringComparer.Ordinal).ToArray();
        await Assert.That(manifestPaths.SequenceEqual(selectedSources.Order(StringComparer.Ordinal), StringComparer.Ordinal)).IsTrue();
        await Assert.That(SiteCoverageModeSelector.CriticalSources(currentMode)
            .All(manifestPaths.Contains)).IsTrue();
    }

    [Test]
    public async Task AC_BC_WEB_005_RejectsUnknownBenchmarkQualificationMode()
    {
        Exception? failure = null;
        try
        {
            _ = SiteCoverageModeSelector.Resolve("latest");
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }

        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(failure!.Message).IsEqualTo(SiteCoverageTokens.InvalidModeFailure);
    }
}
