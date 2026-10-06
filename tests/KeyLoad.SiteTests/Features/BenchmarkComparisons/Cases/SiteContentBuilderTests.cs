using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteContentBuilderTests
{
    [Test]
    public async Task AC_BC_WEB_003_BuildsCompleteSourceBoundSiteWithoutMeasurementArtifacts()
    {
        var inputs = SiteContentInputs.FromEnvironment();
        var token = TestContext.Current!.Execution.CancellationToken;
        await SiteQualificationSource.RequireCheckoutAsync(inputs.Repository, inputs.SiteRevision, token);
        await using var temporary = SiteTempDirectory.Create();
        var build = await SiteContentBuilderProcess.BuildAsync(inputs, temporary.Output, token);

        await Assert.That(build.ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
        await Assert.That(build.StandardError.Length).IsEqualTo(SiteTokens.Zero);
        using var receipt = JsonDocument.Parse(build.StandardOutput);
        await Assert.That(receipt.RootElement.GetProperty(SiteContentTokens.Mode).GetString())
            .IsEqualTo(SiteContentTokens.ContentMode);
        await Assert.That(receipt.RootElement.GetProperty(SiteContentTokens.BenchmarkSourceProperty).GetString())
            .IsEqualTo(SiteContentTokens.BenchmarkSource);
        await Assert.That(receipt.RootElement.GetProperty(SiteContentTokens.SiteRevision).GetString())
            .IsEqualTo(inputs.SiteRevision);
        await Assert.That(receipt.RootElement.TryGetProperty(SiteContentTokens.MeasuredRevision, out _)).IsFalse();
        await Assert.That(receipt.RootElement.TryGetProperty(SiteContentTokens.Cohort, out _)).IsFalse();
        await VerifyAssetsAsync(inputs, temporary.Output, token);
        await VerifyHtmlAsync(temporary.Output, token);
        await VerifyNoMeasurementsAsync(temporary.Output, token);
    }

    [Test]
    public async Task AC_BC_WEB_003_RejectsUnknownModeAndPreservesUnsafeOrCollidingOutputs()
    {
        var inputs = SiteContentInputs.FromEnvironment();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var unknownScope = SiteTempDirectory.Create();
        var unknownOutput = unknownScope.Output;
        var unknown = await SiteContentBuilderProcess.RunAsync(inputs,
            [$"{SiteContentTokens.BuilderModeMarker}=missing", "--output=" + unknownOutput,
                "--site-revision=" + inputs.SiteRevision], token);
        await Assert.That(unknown.ExitCode != SiteTokens.ProcessSuccessExitCode).IsTrue();
        await Assert.That(unknown.StandardError.Contains(SiteContentTokens.ModeError, StringComparison.Ordinal)).IsTrue();
        await Assert.That(Directory.Exists(unknownOutput)).IsFalse();

        await using var mixedScope = SiteTempDirectory.Create();
        var mixed = await SiteContentBuilderProcess.RunAsync(inputs,
            [SiteContentTokens.NoneArgument, "--output=" + mixedScope.Output,
                "--site-revision=" + inputs.SiteRevision, SiteContentTokens.ConflictingMeasuredArgument], token);
        await Assert.That(mixed.ExitCode != SiteTokens.ProcessSuccessExitCode).IsTrue();
        await Assert.That(mixed.StandardError.Contains(SiteContentTokens.InvalidArgumentError, StringComparison.Ordinal)).IsTrue();
        await Assert.That(Directory.Exists(mixedScope.Output)).IsFalse();

        var unsafeOutput = Path.Combine(inputs.Repository, SiteAssetTokens.SiteRootDirectory,
            SiteContentTokens.UnsafeOutputName + SiteContentTokens.UniqueSuffix + Guid.NewGuid().ToString("N"));
        try
        {
            var unsafeBuild = await SiteContentBuilderProcess.BuildAsync(inputs, unsafeOutput, token);
            await Assert.That(unsafeBuild.ExitCode != SiteTokens.ProcessSuccessExitCode).IsTrue();
            await Assert.That(unsafeBuild.StandardError.Contains(SiteContentTokens.OutputError, StringComparison.Ordinal)).IsTrue();
            await Assert.That(Directory.Exists(unsafeOutput)).IsFalse();
        }
        finally
        {
            if (Directory.Exists(unsafeOutput))
            {
                Directory.Delete(unsafeOutput, recursive: true);
            }
        }

        await using var collision = SiteTempDirectory.Create();
        Directory.CreateDirectory(collision.Output);
        var sentinel = Path.Combine(collision.Output, SiteContentTokens.SentinelFile);
        await File.WriteAllTextAsync(sentinel, SiteContentTokens.Sentinel, token);
        var collisionBuild = await SiteContentBuilderProcess.BuildAsync(inputs, collision.Output, token);
        await Assert.That(collisionBuild.ExitCode != SiteTokens.ProcessSuccessExitCode).IsTrue();
        await Assert.That(collisionBuild.StandardError.Contains(SiteContentTokens.OutputError, StringComparison.Ordinal)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(sentinel, token)).IsEqualTo(SiteContentTokens.Sentinel);
    }

    private static async Task VerifyAssetsAsync(SiteContentInputs inputs, string output, CancellationToken token)
    {
        foreach (var relative in SiteContentTokens.ContentModules.Concat(SiteContentTokens.StaticFeatureAssets))
        {
            var source = Path.Combine(inputs.Repository, SiteAssetTokens.FeatureRelativePath, relative);
            var emitted = Path.Combine(output, SiteAssetTokens.EmittedFeatureRelativePath, relative);
            await Assert.That(File.Exists(emitted)).IsTrue();
            var emittedBytes = await File.ReadAllBytesAsync(emitted, token);
            var sourceBytes = await File.ReadAllBytesAsync(source, token);
            await Assert.That(emittedBytes.SequenceEqual(sourceBytes)).IsTrue();
        }

        foreach (var relative in SiteContentTokens.RootAssets)
        {
            var source = relative switch
            {
                "favicon.svg" => Path.Combine(inputs.Repository, SiteAssetTokens.FaviconSourcePath),
                "site.webmanifest" or "og-image.svg" => Path.Combine(inputs.Repository,
                    SiteAssetTokens.FeatureRelativePath, "assets", relative),
                "og-image.png" => Path.Combine(inputs.Repository, SiteAssetTokens.FeatureRelativePath, "assets", relative),
                _ => Path.Combine(inputs.Repository, SiteAssetTokens.SiteRootDirectory, relative),
            };
            var emitted = Path.Combine(output, relative);
            await Assert.That(File.Exists(emitted)).IsTrue();
            var emittedBytes = await File.ReadAllBytesAsync(emitted, token);
            var sourceBytes = await File.ReadAllBytesAsync(source, token);
            await Assert.That(emittedBytes.SequenceEqual(sourceBytes)).IsTrue();
        }

        foreach (var relative in new[] { SiteAssetTokens.WebGpuVendorModule, SiteAssetTokens.CoreVendorModule,
                     SiteAssetTokens.VendorLicense, SiteAssetTokens.ThreeManifestFile })
        {
            var root = Path.Combine(SiteAssetTokens.EmittedFeatureRelativePath, SiteAssetTokens.ThreeVendorRelativePath);
            var source = Path.Combine(inputs.Repository, SiteAssetTokens.FeatureRelativePath,
                SiteAssetTokens.ThreeVendorRelativePath, relative);
            var emitted = Path.Combine(output, root, relative);
            await Assert.That(File.Exists(emitted)).IsTrue();
            var emittedBytes = await File.ReadAllBytesAsync(emitted, token);
            var sourceBytes = await File.ReadAllBytesAsync(source, token);
            await Assert.That(emittedBytes.SequenceEqual(sourceBytes)).IsTrue();
        }

        var inputView = new SiteTestInputs(inputs.Repository, string.Empty, string.Empty, string.Empty, inputs.SiteRevision);
        await SiteBuildArtifacts.CompareVendorManifest(inputView, output, token);
        await VerifyGeneratedAsync(output, token);
    }

    private static async Task VerifyGeneratedAsync(string output, CancellationToken token)
    {
        var generated = new[] { SiteAssetTokens.NoJekyllFile, SiteAssetTokens.CnameFile,
            SiteAssetTokens.RobotsFile, SiteAssetTokens.SitemapFile };
        foreach (var path in generated)
        {
            await Assert.That(File.Exists(Path.Combine(output, path))).IsTrue();
        }

        var robots = await File.ReadAllTextAsync(Path.Combine(output, SiteAssetTokens.RobotsFile), token);
        var sitemap = await File.ReadAllTextAsync(Path.Combine(output, SiteAssetTokens.SitemapFile), token);
        await Assert.That(robots.Contains("Sitemap: https://www.keyload.cloud/sitemap.xml", StringComparison.Ordinal)).IsTrue();
        await Assert.That(sitemap.Contains("https://www.keyload.cloud/", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task VerifyHtmlAsync(string output, CancellationToken token)
    {
        var html = await File.ReadAllTextAsync(Path.Combine(output, SiteAssetTokens.IndexHtml), token);
        await Assert.That(html.Contains(SiteContentTokens.EmptyState, StringComparison.Ordinal)).IsTrue();
        await Assert.That(html.Contains(SiteContentTokens.IsolatedCatalogAttribute, StringComparison.Ordinal)).IsFalse();
        await Assert.That(html.Contains(SiteContentTokens.InstrumentClass, StringComparison.Ordinal)).IsFalse();
        await Assert.That(html.Contains(SiteContentTokens.LoadingClass, StringComparison.Ordinal)).IsFalse();
        await Assert.That(html.Contains(SiteContentTokens.RetryClass, StringComparison.Ordinal)).IsFalse();
        await Assert.That(html.Contains(SiteContentTokens.ProductTitle, StringComparison.Ordinal)).IsTrue();
        await Assert.That(html.Contains(SiteContentTokens.RootMetaCanonical, StringComparison.Ordinal)).IsTrue();
        await Assert.That(html.Contains(SiteContentTokens.DescriptionMeta, StringComparison.Ordinal)).IsTrue();
        await Assert.That(html.Contains(SiteContentTokens.JsonLdType, StringComparison.Ordinal)).IsTrue();
        await Assert.That(html.Contains(SiteAssetTokens.BootstrapModule, StringComparison.Ordinal)).IsTrue();
    }

    private static async Task VerifyNoMeasurementsAsync(string output, CancellationToken token)
    {
        await Assert.That(Directory.Exists(Path.Combine(output, SiteContentTokens.DataDirectory))).IsFalse();
        var files = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).ToArray();
        await Assert.That(files.Any(path => Path.GetFileName(path).Contains(SiteContentTokens.IsolatedCatalogPath,
            StringComparison.Ordinal))).IsFalse();
        await Assert.That(files.Any(path => Path.GetFileName(path) is SiteContentTokens.AggregatePath or
            SiteContentTokens.ProjectionPath)).IsFalse();
        var modules = files.Where(path => Path.GetExtension(path) == SiteContentTokens.ScriptExtension)
            .Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToArray();
        var expected = SiteContentTokens.ContentModules.Order(StringComparer.Ordinal).ToArray();
        await Assert.That(modules.SequenceEqual(expected, StringComparer.Ordinal)).IsTrue();
        foreach (var path in files.Where(path => Path.GetExtension(path) is SiteContentTokens.ScriptExtension or
                     SiteContentTokens.HtmlExtension))
        {
            var content = await File.ReadAllTextAsync(path, token);
            await Assert.That(content.Contains(SiteContentTokens.FetchCall, StringComparison.Ordinal)).IsFalse();
            await Assert.That(content.Contains(SiteContentTokens.XmlHttpRequest, StringComparison.Ordinal)).IsFalse();
            await Assert.That(content.Contains(SiteContentTokens.WebSocket, StringComparison.Ordinal)).IsFalse();
        }
    }
}
