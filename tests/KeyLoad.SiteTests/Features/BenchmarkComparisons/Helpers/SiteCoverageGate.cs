using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteCoverageGate
{
    private static readonly string[] CriticalSources =
    [
        $"{SiteCoverageTokens.FeatureSourcePrefix}isolated-contracts.mjs",
        $"{SiteCoverageTokens.FeatureSourcePrefix}isolated-metadata.mjs",
        $"{SiteCoverageTokens.FeatureSourcePrefix}isolated-metrics-validation.mjs",
        $"{SiteCoverageTokens.FeatureSourcePrefix}isolated-report-validation.mjs",
        $"{SiteCoverageTokens.FeatureSourcePrefix}isolated-projection.mjs",
        $"{SiteCoverageTokens.FeatureSourcePrefix}isolated-http.mjs",
        $"{SiteCoverageTokens.FeatureSourcePrefix}isolated-loader.mjs",
        $"{SiteCoverageTokens.FeatureSourcePrefix}isolated-measurements.mjs",
        $"{SiteCoverageTokens.FeatureSourcePrefix}isolated-view.mjs",
        $"{SiteCoverageTokens.FeatureSourcePrefix}isolated-controls.mjs",
        $"{SiteCoverageTokens.FeatureSourcePrefix}isolated-lab.mjs",
        $"{SiteCoverageTokens.FeatureSourcePrefix}measurements.mjs",
        $"{SiteCoverageTokens.FeatureSourcePrefix}measurement-loader.mjs",
        $"{SiteCoverageTokens.FeatureSourcePrefix}build-site.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-api.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-capture.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-cli.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-context.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-contract.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-files.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-fresh.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-native-proof.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-proof.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-receipt.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-runs.mjs",
    ];

    private static SiteIsolatedGitHubArchiveReceipt? isolatedArchiveReceipt;

    [Before(HookType.TestSession)]
    public static async Task CaptureSourceBaselineAsync()
    {
        var repository = RequiredPath(SiteTokens.RepositoryEnvironment);
        var revision = Environment.GetEnvironmentVariable(SitePublicationTokens.SourceRevisionEnvironment);
        await SiteQualificationSource.RequireCheckoutAsync(repository, revision ?? string.Empty, CancellationToken.None);
        isolatedArchiveReceipt = await SiteIsolatedGitHubArchiveSetup.PrepareFromEnvironmentAsync(CancellationToken.None);
        await SiteCoverageSourceManifestWriter.CaptureAsync();
    }

    [After(HookType.TestSession)]
    public static async Task JoinAndEnforceCoverageAsync()
    {
        var repository = RequiredPath(SiteTokens.RepositoryEnvironment);
        var artifactRoot = RequiredPath(SiteCoverageTokens.CoverageRootEnvironment);
        var manifestHash = await SiteCoverageSourceManifestWriter.VerifyManifestAsync(artifactRoot).ConfigureAwait(false);
        var manifest = await ReadManifestAsync(artifactRoot).ConfigureAwait(false);
        if (Environment.GetEnvironmentVariable(SitePublicationTokens.SourceRevisionEnvironment) != manifest.SourceRevision)
        {
            throw new InvalidOperationException(SiteCoverageTokens.SourceMismatchFailure);
        }

        await SiteCoverageSourceManifestWriter.VerifyUnchangedAsync(repository, manifest).ConfigureAwait(false);
        await SiteIsolatedGitHubArchiveSetup.VerifyUnchangedAsync(isolatedArchiveReceipt ??
            throw new InvalidOperationException(SitePublicationTokens.MissingArchivePreparation), CancellationToken.None);
        var collection = await SiteCoverageArtifactReader.ReadAsync(repository, artifactRoot, manifest,
            CancellationToken.None).ConfigureAwait(false);
        var fileResults = AnalyzeFiles(repository, manifest, collection);
        var totals = SiteCoverageAnalyzer.Sum(fileResults);
        var critical = AnalyzeCritical(fileResults);
        var passed = IsPassing(collection.Errors, totals, critical);
        var report = CreateReport(manifest, manifestHash, collection, fileResults, totals, critical, passed);
        await WriteReportAsync(artifactRoot, report).ConfigureAwait(false);
        if (!passed)
        {
            throw new InvalidOperationException(SiteCoverageTokens.ThresholdFailure);
        }
    }

    private static List<SiteCoverageFileResult> AnalyzeFiles(string repository,
        SiteCoverageSourceManifest manifest, SiteCoverageCollection collection)
    {
        var results = new List<SiteCoverageFileResult>(manifest.Sources.Count);
        foreach (var source in manifest.Sources.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            if (!collection.SnapshotsBySource.TryGetValue(source.Path, out var snapshots) ||
                snapshots.Sum(snapshot => snapshot.Functions.Count) == SiteCoverageTokens.Zero)
            {
                collection.Errors.Add($"{source.Path}{SiteCoverageTokens.ErrorSeparator}{SiteCoverageTokens.MissingCoverageFailure}");
                snapshots = [];
            }

            results.Add(SiteCoverageAnalyzer.Analyze(source, repository, snapshots));
        }

        return results;
    }

    private static SiteCoverageCriticalResult[] AnalyzeCritical(
        IReadOnlyList<SiteCoverageFileResult> files)
    {
        return CriticalSources.Select(path =>
        {
            var file = files.SingleOrDefault(item => item.Path == path);
            var percent = file is null ? SiteCoverageTokens.Zero : Percent(file.CoveredLines, file.ExecutableLines);
            return new SiteCoverageCriticalResult(path, percent,
                file is not null && Meets(file.CoveredLines, file.ExecutableLines,
                    SiteCoverageTokens.CriticalLinePercent));
        }).ToArray();
    }

    private static bool IsPassing(IEnumerable<string> errors, SiteCoverageTotals totals,
        IReadOnlyList<SiteCoverageCriticalResult> critical)
    {
        return !errors.Any() && Meets(totals.CoveredLines, totals.ExecutableLines,
                SiteCoverageTokens.AggregateLinePercent) && totals.BlockOutcomes > SiteCoverageTokens.Zero &&
            Meets(totals.CoveredBlockOutcomes, totals.BlockOutcomes, SiteCoverageTokens.AggregateBranchPercent) &&
            critical.All(item => item.Passed);
    }

    private static object CreateReport(SiteCoverageSourceManifest manifest, string manifestHash,
        SiteCoverageCollection collection,
        IReadOnlyList<SiteCoverageFileResult> files, SiteCoverageTotals totals,
        IReadOnlyList<SiteCoverageCriticalResult> critical, bool passed)
    {
        return new
        {
            schemaVersion = SiteCoverageTokens.Schema,
            sourceRevision = manifest.SourceRevision,
            sourceManifestSha256 = manifestHash,
            nodeVersion = manifest.NodeVersion,
            lineSemantics = SiteCoverageTokens.LineSemantics,
            branchSemantics = SiteCoverageTokens.BranchSemantics,
            thresholds = new
            {
                aggregateLinePercent = SiteCoverageTokens.AggregateLinePercent,
                aggregateBranchPercent = SiteCoverageTokens.AggregateBranchPercent,
                criticalLinePercent = SiteCoverageTokens.CriticalLinePercent,
                criticalSources = CriticalSources,
            },
            files,
            totals = new
            {
                totals.ExecutableLines,
                totals.CoveredLines,
                aggregateLinesPercent = Percent(totals.CoveredLines, totals.ExecutableLines),
                totals.BlockOutcomes,
                totals.CoveredBlockOutcomes,
                aggregateBranchesPercent = Percent(totals.CoveredBlockOutcomes, totals.BlockOutcomes),
            },
            critical,
            receipts = collection.Receipts.OrderBy(item => item.Path, StringComparer.Ordinal),
            browserSessions = collection.BrowserSessions.OrderBy(item => item.Id, StringComparer.Ordinal),
            errors = collection.Errors,
            passed,
        };
    }

    private static async Task WriteReportAsync(string artifactRoot, object report)
    {
        var path = Path.Combine(artifactRoot, SiteCoverageTokens.ReportFile);
        var pending = path + SiteCoverageTokens.TemporaryReportSuffix;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, SiteCoverageTokens.JsonOptions);
        await using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(bytes).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        File.Move(pending, path);
    }

    private static async Task<SiteCoverageSourceManifest> ReadManifestAsync(string artifactRoot)
    {
        var path = Path.Combine(artifactRoot, SiteCoverageTokens.SourceManifestFile);
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<SiteCoverageSourceManifest>(bytes, SiteCoverageTokens.JsonOptions);
        if (manifest is null || manifest.SchemaVersion != SiteCoverageTokens.Schema ||
            !SiteCoverageSourceManifestWriter.IsRevision(manifest.SourceRevision) || manifest.Sources.Count != SiteCoverageTokens.ProductionSources.Length ||
            !manifest.NodeVersion.StartsWith(SiteCoverageTokens.NodeVersionPrefix, StringComparison.Ordinal) ||
            !manifest.Sources.Select(item => item.Path).Order(StringComparer.Ordinal)
                .SequenceEqual(SiteCoverageTokens.ProductionSources.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException(SiteCoverageTokens.InvalidRootFailure);
        }

        if (manifest.Sources.Any(source => !SiteCoverageSourceManifestWriter.IsHash(source.Sha256) ||
            source.Utf16Length <= SiteCoverageTokens.Zero || source.Utf16Length > SiteCoverageTokens.MaximumUtf16SourceLength))
        {
            throw new InvalidOperationException(SiteCoverageTokens.InvalidRootFailure);
        }

        return manifest;
    }

    private static string RequiredPath(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return value is not null && Path.IsPathFullyQualified(value) && Directory.Exists(value)
            ? Path.GetFullPath(value) : throw new InvalidOperationException(SiteCoverageTokens.InvalidRootFailure);
    }

    private static int Percent(int covered, int total) => total == SiteCoverageTokens.Zero
        ? SiteCoverageTokens.Zero : (int)((long)covered * SiteTokens.ErrorPercentageScale / total);

    private static bool Meets(int covered, int total, int threshold) => total > SiteCoverageTokens.Zero &&
        (long)covered * SiteTokens.ErrorPercentageScale >= (long)total * threshold;
}

internal sealed record SiteCoverageCriticalResult(string Path, int CoveragePercent, bool Passed);
