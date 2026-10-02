using System.Globalization;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Verifies the real static-site builder against isolated outputs and authentic evidence.</summary>
internal sealed class SiteBuildTests
{
    /// <summary>Checks no-script evidence, source provenance, raw report bytes, and emitted assets.</summary>
    [Test]
    public async Task AC_BC_016_RealBuilderEmitsCompletePreviewWithAuthenticRawBytesAndNoScriptEvidence()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var build = await SiteBuilderProcess.RunAsync(inputs, inputs.Reports, temporary.Output, token,
            committedSource: false);
        await Assert.That(build.ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
        await Assert.That(build.StandardError.Length).IsEqualTo(SiteTokens.Zero);
        using var buildResult = JsonDocument.Parse(build.StandardOutput);
        await Assert.That(buildResult.RootElement.GetProperty(SiteAssetTokens.Profiles).GetInt32()).IsEqualTo(SiteTokens.ProfileNames.Length);
        await AssertNoScriptEvidence(inputs, temporary.Output, token);
        await AssertCatalogProvenance(inputs, temporary.Output, token);
        await SiteBuildArtifacts.AssertRawReports(inputs, temporary.Output, token);
        await SiteBuildArtifacts.CompareEmittedAssets(inputs, temporary.Output, token);
        await SiteBuildArtifacts.CompareVendorManifest(inputs, temporary.Output, token);
    }

    /// <summary>Passes the runner's source SHA separately and checks revision-pinned documentation links.</summary>
    [Test]
    public async Task AC_BC_016_CommittedSourceOutputPreservesBothSiteAndMeasuredRevisions()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var build = await SiteBuilderProcess.RunAsync(inputs, inputs.Reports, temporary.Output, token);
        await Assert.That(build.ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
        using var catalog = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(temporary.Output, SiteTokens.DataDirectory, SiteTokens.CatalogFile), token));
        var root = catalog.RootElement;
        await Assert.That(root.GetProperty(SiteTokens.SiteSourceKind).GetString()).IsEqualTo(SiteTokens.CommittedSourceKind);
        await Assert.That(root.GetProperty(SiteTokens.SiteSourceRevision).GetString()).IsEqualTo(inputs.SiteRevision);
        await Assert.That(root.GetProperty(SiteTokens.MeasuredSourceRevision).GetString()).IsEqualTo(inputs.MeasuredRevision);
        var html = await File.ReadAllTextAsync(Path.Combine(temporary.Output, SiteAssetTokens.IndexHtml), token);
        var architectureUrl = $"{SiteTokens.RepositoryWebBase}{SiteTokens.DocumentationBlobPath}{inputs.SiteRevision}/{SiteTokens.ArchitectureDocPath}";
        var docsTreeUrl = $"{SiteTokens.RepositoryWebBase}{SiteTokens.DocumentationTreePath}{inputs.SiteRevision}/{SiteTokens.DocsDirectory}";
        await Assert.That(html.Contains(architectureUrl, StringComparison.Ordinal)).IsTrue();
        await Assert.That(html.Contains(docsTreeUrl, StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertNoScriptEvidence(SiteTestInputs inputs, string output, CancellationToken token)
    {
        await Assert.That(File.Exists(Path.Combine(output, SiteAssetTokens.IndexHtml))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(output, SiteAssetTokens.FaviconSvg))).IsTrue();
        var html = await File.ReadAllTextAsync(Path.Combine(output, SiteAssetTokens.IndexHtml), token);
        await Assert.That(html.Contains(SiteAssetTokens.NoScriptTag, StringComparison.Ordinal)).IsTrue();
        await Assert.That(html.Contains(SiteAssetTokens.StaticEvidenceMarker, StringComparison.Ordinal)).IsFalse();
        foreach (var profile in SiteTokens.ProfileNames)
        {
            var path = $"./{SiteTokens.DataDirectory}/{SiteTokens.RunsDirectory}/{profile}/{SiteTokens.ReportFile}";
            await Assert.That(html.Contains(path, StringComparison.Ordinal)).IsTrue();
            await Assert.That(html.Contains(inputs.EvidenceUrl, StringComparison.Ordinal)).IsTrue();
        }
    }

    private static async Task AssertCatalogProvenance(SiteTestInputs inputs, string output, CancellationToken token)
    {
        var catalogPath = Path.Combine(output, SiteTokens.DataDirectory, SiteTokens.CatalogFile);
        using var catalog = JsonDocument.Parse(await File.ReadAllBytesAsync(catalogPath, token));
        var root = catalog.RootElement;
        await Assert.That(root.GetProperty(SiteTokens.SchemaVersion).GetInt32()).IsEqualTo(SiteTokens.CatalogSchemaNumber);
        await Assert.That(root.GetProperty(SiteTokens.SiteSourceKind).GetString()).IsEqualTo(SiteTokens.PreviewKind);
        await Assert.That(root.GetProperty(SiteTokens.SiteSourceRevision).ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(root.GetProperty(SiteTokens.MeasuredSourceRevision).GetString()).IsEqualTo(inputs.MeasuredRevision);
        await Assert.That(root.GetProperty(SiteTokens.EvidenceUrl).GetString()).IsEqualTo(inputs.EvidenceUrl);
        await Assert.That(root.GetProperty(SiteTokens.Runs).GetArrayLength()).IsEqualTo(SiteTokens.ProfileNames.Length);
        await Assert.That(DateTimeOffset.TryParse(root.GetProperty(SiteTokens.GeneratedAt).GetString(),
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)).IsTrue();
        var runs = root.GetProperty(SiteTokens.Runs);
        var expectedProfiles = new[] { SiteTokens.LargeProfile, SiteTokens.SmallProfile, SiteTokens.SmokeProfile };
        var actualProfiles = runs.EnumerateArray()
            .Select(entry => entry.GetProperty(SiteTokens.Id).GetString()!)
            .ToArray();
        await Assert.That(actualProfiles.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(expectedProfiles.Length);
        await Assert.That(new HashSet<string>(actualProfiles, StringComparer.Ordinal).SetEquals(expectedProfiles)).IsTrue();
        await Assert.That(actualProfiles.SequenceEqual(expectedProfiles, StringComparer.Ordinal)).IsTrue();
        foreach (var profile in expectedProfiles)
        {
            var entry = runs.EnumerateArray().Single(candidate =>
                string.Equals(candidate.GetProperty(SiteTokens.Id).GetString(), profile, StringComparison.Ordinal));
            await Assert.That(string.IsNullOrWhiteSpace(entry.GetProperty(SiteTokens.Label).GetString())).IsFalse();
            await Assert.That(entry.GetProperty(SiteTokens.Report).GetString()).IsEqualTo(
                $"{SiteTokens.RunsDirectory}/{profile}/{SiteTokens.ReportFile}");
            await Assert.That(entry.GetProperty(SiteTokens.SourceRevision).GetString()).IsEqualTo(inputs.MeasuredRevision);
            await Assert.That(entry.GetProperty(SiteTokens.EvidenceUrl).GetString()).IsEqualTo(inputs.EvidenceUrl);
            var hash = await SiteBuildArtifacts.Sha256File(Path.Combine(inputs.Reports, profile, SiteTokens.ReportFile), token);
            await Assert.That(entry.GetProperty(SiteTokens.Sha256).GetString()).IsEqualTo(hash);
            await Assert.That(DateTimeOffset.TryParse(entry.GetProperty(SiteTokens.StartedAt).GetString(),
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)).IsTrue();
        }
    }

    /// <summary>Checks existing and source-descendant output paths are refused without residue.</summary>
    [Test]
    public async Task AC_BC_016_ExistingAndDescendantOutputsAreRefusedWithoutResidue()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        Directory.CreateDirectory(temporary.Output);
        var sentinel = Path.Combine(temporary.Output, SiteAssetTokens.SentinelFile);
        await File.WriteAllTextAsync(sentinel, SiteAssetTokens.SentinelValue, token);
        var existing = await SiteBuilderProcess.RunAsync(inputs, inputs.Reports, temporary.Output, token);
        await Assert.That(existing.ExitCode != SiteTokens.ProcessSuccessExitCode).IsTrue();
        await AssertBuilderError(existing, SiteBuilderTokens.OutputPathError);
        await Assert.That(await File.ReadAllTextAsync(sentinel, token)).IsEqualTo(SiteAssetTokens.SentinelValue);

        var siteRoot = Path.Combine(inputs.Repository, SiteAssetTokens.SiteRootDirectory);
        foreach (var parent in new[] { siteRoot, inputs.Reports })
        {
            var nested = Path.Combine(parent, $"{SiteAssetTokens.PreviewDirectoryPrefix}{Guid.NewGuid():N}");
            var rejected = await SiteBuilderProcess.RunAsync(inputs, inputs.Reports, nested, token);
            await Assert.That(rejected.ExitCode != SiteTokens.ProcessSuccessExitCode).IsTrue();
            await AssertBuilderError(rejected, SiteBuilderTokens.OutputPathError);
            await Assert.That(Directory.Exists(nested)).IsFalse();
        }
    }

    /// <summary>Checks symlink and malformed-input failures leave no build output.</summary>
    [Test]
    public async Task AC_BC_016_SymlinkAndCorruptInputsAreRejectedBeforeOutputCreation()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var realParent = Path.Combine(temporary.Path, SiteAssetTokens.RealParentDirectory);
        Directory.CreateDirectory(realParent);
        var linkedParent = Path.Combine(temporary.Path, SiteAssetTokens.LinkedParentDirectory);
        Directory.CreateSymbolicLink(linkedParent, realParent);
        var outputThroughLink = Path.Combine(linkedParent, SiteAssetTokens.OutputDirectory);
        var linked = await SiteBuilderProcess.RunAsync(inputs, inputs.Reports, outputThroughLink, token);
        await Assert.That(linked.ExitCode != SiteTokens.ProcessSuccessExitCode).IsTrue();
        await AssertBuilderError(linked, SiteBuilderTokens.SymlinkError);
        await Assert.That(Directory.Exists(Path.Combine(realParent, SiteAssetTokens.OutputDirectory))).IsFalse();

        Directory.CreateDirectory(temporary.Reports);
        foreach (var profile in SiteTokens.ProfileNames)
        {
            Directory.CreateSymbolicLink(Path.Combine(temporary.Reports, profile), Path.Combine(inputs.Reports, profile));
        }

        var linkedInputOutput = Path.Combine(temporary.Path, SiteAssetTokens.LinkedInputOutput);
        var linkedInput = await SiteBuilderProcess.RunAsync(inputs, temporary.Reports, linkedInputOutput, token);
        await Assert.That(linkedInput.ExitCode != SiteTokens.ProcessSuccessExitCode).IsTrue();
        await AssertBuilderError(linkedInput, SiteBuilderTokens.SymlinkError);
        await Assert.That(Directory.Exists(linkedInputOutput)).IsFalse();

        await using var corrupt = SiteTempDirectory.Create();
        await SiteBuildArtifacts.CopyReports(inputs.Reports, corrupt.Reports, token);
        await File.WriteAllTextAsync(Path.Combine(corrupt.Reports, SiteTokens.SmokeProfile, SiteTokens.ReportFile), SiteTokens.InvalidJson, token);
        var failedOutput = Path.Combine(corrupt.Path, SiteAssetTokens.FailedOutput);
        var failed = await SiteBuilderProcess.RunAsync(inputs, corrupt.Reports, failedOutput, token);
        await Assert.That(failed.ExitCode != SiteTokens.ProcessSuccessExitCode).IsTrue();
        await AssertBuilderError(failed, SiteBuilderTokens.JsonParserMarker);
        await Assert.That(failed.StandardError.Contains(SiteBuilderTokens.VendorError, StringComparison.Ordinal)).IsFalse();
        await Assert.That(Directory.Exists(failedOutput)).IsFalse();
    }

    private static async Task AssertBuilderError(SiteProcessResult result, string expected)
    {
        await Assert.That(result.StandardError.Contains(expected, StringComparison.Ordinal)).IsTrue();
    }

}
