using System.Text.Json;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal static class SiteCoverageArtifactReader
{
    public static async Task<SiteCoverageCollection> ReadAsync(string repository, string artifactRoot,
        SiteCoverageSourceManifest manifest, CancellationToken cancellationToken)
    {
        var result = new SiteCoverageCollection(artifactRoot);
        var sources = manifest.Sources.ToDictionary(source => source.Path, StringComparer.Ordinal);
        var nodeRoot = Path.Combine(artifactRoot, SiteCoverageTokens.NodeDirectory);
        var browserRoot = Path.Combine(artifactRoot, SiteCoverageTokens.BrowserDirectory,
            SiteCoverageTokens.SessionsDirectory);
        await ReadNodeFilesAsync(nodeRoot, repository, sources, manifest.NodeVersion, result, cancellationToken)
            .ConfigureAwait(false);
        await ReadBrowserSessionsAsync(browserRoot, repository, sources, manifest, result, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Receipts.Any(receipt => receipt.Runtime == SiteCoverageTokens.NodeRuntime) ||
            !result.Receipts.Any(receipt => receipt.Runtime == SiteCoverageTokens.BrowserRuntime) ||
            result.BrowserSessions.Count == SiteCoverageTokens.Zero)
        {
            result.Errors.Add(SiteCoverageTokens.MissingReceiptsFailure);
        }

        return result;
    }

    private static async Task ReadNodeFilesAsync(string directory, string repository,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources, string version, SiteCoverageCollection result,
        CancellationToken cancellationToken)
    {
        var files = SiteCoverageArtifactFiles.EnumerateFiles(directory, result);
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (!name.StartsWith(SiteCoverageTokens.NodeCoverageFilePrefix, StringComparison.Ordinal) ||
                Path.GetExtension(name) != SiteCoverageTokens.JsonExtension)
            {
                result.Errors.Add(SiteCoverageTokens.ExtensionFailure);
                continue;
            }

            await ReadFileAsync(file, repository, sources, null, SiteCoverageTokens.NodeRuntime, version,
                result, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ReadBrowserSessionsAsync(string directory, string repository,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources, SiteCoverageSourceManifest manifest,
        SiteCoverageCollection result, CancellationToken cancellationToken)
    {
        foreach (var sessionDirectory in SiteCoverageArtifactFiles.EnumerateDirectories(directory, result))
        {
            var id = Path.GetFileName(sessionDirectory);
            if (!SiteCoverageArtifactFiles.IsSessionId(id))
            {
                result.Errors.Add(SiteCoverageTokens.InvalidMetadataFailure);
                continue;
            }

            await ReadBrowserSessionAsync(sessionDirectory, id, repository, sources, manifest, result, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task ReadBrowserSessionAsync(string directory, string id, string repository,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources, SiteCoverageSourceManifest manifest,
        SiteCoverageCollection result, CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(directory, SiteCoverageTokens.MetadataFile);
        if (!File.Exists(metadataPath))
        {
            result.Errors.Add(SiteCoverageTokens.InvalidMetadataFailure);
            return;
        }

        try
        {
            var metadataBytes = await SiteCoverageArtifactFiles.ReadBoundedAsync(metadataPath, result, cancellationToken).ConfigureAwait(false);
            var metadata = SiteCoverageArtifactMetadata.ParseMetadata(metadataBytes, manifest, sources);
            var listedNames = metadata.CoverageFiles.ToHashSet(StringComparer.Ordinal);
            SiteCoverageArtifactMetadata.ValidateSessionFiles(directory, listedNames);
            var artifactRoot = result.ArtifactRoot;
            var relativeMetadata = SiteCoverageArtifactFiles.RelativePath(artifactRoot, metadataPath);
            var firstReceiptIndex = result.Receipts.Count;
            var receiptPaths = new List<string>();
            foreach (var name in metadata.CoverageFiles.Order(StringComparer.Ordinal))
            {
                var coveragePath = Path.Combine(directory, name);
                await ReadFileAsync(coveragePath, repository, sources, metadata.Origins,
                    SiteCoverageTokens.BrowserRuntime, metadata.BrowserVersion, result, cancellationToken)
                    .ConfigureAwait(false);
                receiptPaths.Add(SiteCoverageArtifactFiles.RelativePath(artifactRoot, coveragePath));
            }

            var mappedFunctions = result.Receipts.Skip(firstReceiptIndex).Sum(receipt => receipt.MappedFunctions);
            if (mappedFunctions == SiteCoverageTokens.Zero)
            {
                result.Errors.Add(
                    $"{relativeMetadata}{SiteCoverageTokens.ErrorSeparator}{SiteCoverageTokens.MissingBrowserSessionRangesFailure}");
            }

            result.BrowserSessions.Add(new(id, metadata.BrowserVersion, relativeMetadata,
                SiteCoverageSourceManifestWriter.Hash(metadataBytes), metadata.Origins.ToArray(), receiptPaths));
        }
        catch (Exception exception) when (SiteCoverageArtifactFiles.IsCoverageReadFailure(exception))
        {
            result.Errors.Add($"{SiteCoverageArtifactFiles.RelativePath(result.ArtifactRoot, metadataPath)}{SiteCoverageTokens.ErrorSeparator}{exception.Message}");
        }
    }

    private static async Task ReadFileAsync(string path, string repository,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources, IReadOnlySet<string>? origins,
        string runtime, string version, SiteCoverageCollection result, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await SiteCoverageArtifactFiles.ReadBoundedAsync(path, result, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes, SiteCoverageTokens.JsonDocumentOptions);
            SiteCoverageArtifactMetadata.ValidateEnvelope(document.RootElement, runtime);
            var artifactRoot = result.ArtifactRoot;
            var receiptIdentity = SiteCoverageArtifactFiles.RelativePath(artifactRoot, path);
            var receiptHash = SiteCoverageSourceManifestWriter.Hash(bytes);
            var snapshots = SiteCoverageRanges.ParseScripts(document.RootElement.GetProperty(SiteCoverageTokens.Result),
                runtime, repository, sources, origins, receiptIdentity, receiptHash);
            foreach (var group in snapshots.GroupBy(snapshot => snapshot.SourcePath, StringComparer.Ordinal))
            {
                if (!result.SnapshotsBySource.TryGetValue(group.Key, out var current))
                {
                    current = [];
                    result.SnapshotsBySource.Add(group.Key, current);
                }

                current.AddRange(group);
            }

            result.Receipts.Add(new(receiptIdentity, receiptHash, runtime, version,
                snapshots.Sum(snapshot => snapshot.Functions.Count)));
        }
        catch (Exception exception) when (SiteCoverageArtifactFiles.IsCoverageReadFailure(exception))
        {
            var artifactRoot = result.ArtifactRoot;
            result.Errors.Add($"{SiteCoverageArtifactFiles.RelativePath(artifactRoot, path)}{SiteCoverageTokens.ErrorSeparator}{exception.Message}");
        }
    }

}
