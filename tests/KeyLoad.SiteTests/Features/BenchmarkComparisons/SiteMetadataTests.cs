using System.Text;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteMetadataTests
{
    private static readonly (string File, int Size)[] PngAssets =
    [
        (SiteMetadataTokens.Favicon32, SiteMetadataTokens.PngDeclared32),
        (SiteMetadataTokens.Favicon96, SiteMetadataTokens.PngDeclared96),
        (SiteMetadataTokens.AppleTouch, SiteMetadataTokens.PngDeclared180),
        (SiteMetadataTokens.Icon192, SiteMetadataTokens.PngDeclared192),
        (SiteMetadataTokens.Icon512, SiteMetadataTokens.PngDeclared512),
    ];
    private static readonly string[] MetadataAssetNames =
    [
        SiteMetadataTokens.FaviconIco, SiteMetadataTokens.Favicon32, SiteMetadataTokens.Favicon96,
        SiteMetadataTokens.AppleTouch, SiteMetadataTokens.Icon192, SiteMetadataTokens.Icon512,
    ];

    [Test]
    public async Task AC_SEO_001_IconsAreValidSharedBytesAndEmittedFromTheRealBuild()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var result = await SiteBuilderProcess.RunAsync(inputs, inputs.Reports, temporary.Output, token);
        await Assert.That(result.ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);

        var consoleRoot = Path.Combine(inputs.Repository, SiteMetadataTokens.ConsoleAssetsPath);
        var siteRoot = Path.Combine(inputs.Repository, SiteAssetTokens.SiteRootDirectory);
        var consoleHtml = await File.ReadAllTextAsync(Path.Combine(consoleRoot, SiteMetadataTokens.HtmlFile), token);
        await Assert.That(consoleHtml).Contains(SiteMetadataTokens.ConsoleNoIndex);
        await Assert.That(consoleHtml).Contains(SiteMetadataTokens.ThemeColor);
        await Assert.That(consoleHtml.Contains(SiteMetadataTokens.ConsoleCanonicalMarkup, StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(consoleHtml.Contains(SiteMetadataTokens.ConsoleOgMarkup, StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(consoleHtml.Contains(SiteMetadataTokens.JsonLdMime, StringComparison.OrdinalIgnoreCase)).IsFalse();
        foreach (var link in new[] { SiteMetadataTokens.ConsoleFaviconSvg, SiteMetadataTokens.ConsoleFaviconIco,
                     SiteMetadataTokens.ConsoleFavicon32, SiteMetadataTokens.ConsoleFavicon96, SiteMetadataTokens.ConsoleTouchIcon })
        {
            await Assert.That(consoleHtml.Contains(link, StringComparison.Ordinal)).IsTrue();
        }
        var sourceLogo = await File.ReadAllBytesAsync(Path.Combine(consoleRoot, SiteMetadataTokens.SourceSvgLogo), token);
        await SiteMetadataBinaryAssertions.AssertBytesEqual(sourceLogo, await File.ReadAllBytesAsync(Path.Combine(siteRoot, SiteAssetTokens.FaviconSvg), token));
        await SiteMetadataBinaryAssertions.AssertBytesEqual(sourceLogo, await File.ReadAllBytesAsync(Path.Combine(temporary.Output, SiteAssetTokens.FaviconSvg), token));

        foreach (var asset in MetadataAssetNames)
        {
            var canonical = await File.ReadAllBytesAsync(Path.Combine(consoleRoot, asset), token);
            await SiteMetadataBinaryAssertions.AssertBytesEqual(canonical, await File.ReadAllBytesAsync(Path.Combine(siteRoot, asset), token));
            await SiteMetadataBinaryAssertions.AssertBytesEqual(canonical, await File.ReadAllBytesAsync(Path.Combine(temporary.Output, asset), token));
        }

        foreach (var (file, size) in PngAssets)
        {
            await SiteMetadataBinaryAssertions.AssertPngDimensions(Path.Combine(consoleRoot, file), size, token);
        }

        await SiteMetadataBinaryAssertions.AssertIcoFrames(Path.Combine(consoleRoot, SiteMetadataTokens.FaviconIco), token);
        var outputCard = await File.ReadAllBytesAsync(Path.Combine(temporary.Output, SiteMetadataTokens.OutputCard), token);
        var sourceCard = await File.ReadAllBytesAsync(Path.Combine(inputs.Repository, SiteMetadataTokens.FeaturePath,
            SiteMetadataTokens.SourceCardPng), token);
        await SiteMetadataBinaryAssertions.AssertBytesEqual(sourceCard, outputCard);
        await SiteMetadataBinaryAssertions.AssertPngDimensions(Path.Combine(temporary.Output, SiteMetadataTokens.OutputCard),
            SiteMetadataTokens.OgCardWidth, SiteMetadataTokens.OgCardHeight, token);
        await Assert.That(outputCard.Length).IsLessThan(SiteMetadataTokens.OgCardMaximumBytes);
    }

    [Test]
    public async Task AC_SEO_002_003_RealBuilderOutputHasOneAccurateDiscoverableStaticHead()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var result = await SiteBuilderProcess.RunAsync(inputs, inputs.Reports, temporary.Output, token);
        await Assert.That(result.ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
        var html = await File.ReadAllTextAsync(Path.Combine(temporary.Output, SiteMetadataTokens.HtmlFile), token);
        await SiteMetadataDocumentAssertions.VerifyAsync(html, temporary.Output, token);
    }

    [Test]
    public async Task AC_SEO_004_SourceCardAndManifestAreEmittedAsDeclared()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var result = await SiteBuilderProcess.RunAsync(inputs, inputs.Reports, temporary.Output, token);
        await Assert.That(result.ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
        var feature = Path.Combine(inputs.Repository, SiteMetadataTokens.FeaturePath);
        var cardSvg = await File.ReadAllBytesAsync(Path.Combine(feature, SiteMetadataTokens.SourceCardSvg), token);
        await Assert.That(cardSvg.Length).IsGreaterThan(0);
        await Assert.That(Encoding.UTF8.GetString(cardSvg)).Contains("KeyLoad");
        await Assert.That(Encoding.UTF8.GetString(cardSvg)).Contains("AI agents");
        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(temporary.Output, SiteMetadataTokens.OutputManifest), token));
        var root = manifest.RootElement;
        await Assert.That(root.GetProperty(SiteMetadataTokens.ManifestName).GetString()).IsEqualTo("KeyLoad");
        await Assert.That(root.GetProperty(SiteMetadataTokens.StartUrl).GetString()).IsEqualTo(SiteMetadataTokens.ManifestStartUrl);
        await Assert.That(root.GetProperty(SiteMetadataTokens.Scope).GetString()).IsEqualTo(SiteMetadataTokens.ManifestScope);
        await Assert.That(root.GetProperty(SiteMetadataTokens.Display).GetString()).IsEqualTo(SiteMetadataTokens.ManifestDisplay);
        await Assert.That(root.GetProperty(SiteMetadataTokens.ManifestThemeColor).GetString()).IsEqualTo(SiteMetadataTokens.ThemeColor);
        await Assert.That(root.GetProperty(SiteMetadataTokens.ManifestBackgroundColor).GetString()).IsEqualTo(SiteMetadataTokens.ManifestBackground);
        var icons = root.GetProperty(SiteMetadataTokens.Icons).EnumerateArray().ToArray();
        await Assert.That(icons.Length).IsEqualTo(2);
        var icon192 = icons.Single(item => item.GetProperty(SiteMetadataTokens.Src).GetString() == SiteMetadataTokens.Icon192);
        var icon512 = icons.Single(item => item.GetProperty(SiteMetadataTokens.Src).GetString() == SiteMetadataTokens.Icon512);
        await Assert.That(icon192.GetProperty(SiteMetadataTokens.ManifestSizes).GetString()).IsEqualTo("192x192");
        await Assert.That(icon512.GetProperty(SiteMetadataTokens.ManifestSizes).GetString()).IsEqualTo("512x512");
        await Assert.That(icons.All(item => item.GetProperty(SiteMetadataTokens.ManifestType).GetString() == SiteMetadataTokens.IconTypePng)).IsTrue();
        var sourceManifest = await File.ReadAllBytesAsync(Path.Combine(feature, SiteMetadataTokens.ManifestPath), token);
        var builtManifest = await File.ReadAllBytesAsync(Path.Combine(temporary.Output, SiteMetadataTokens.OutputManifest), token);
        await SiteMetadataBinaryAssertions.AssertBytesEqual(sourceManifest, builtManifest);
    }

}
