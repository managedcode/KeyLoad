using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteCoverageReceipt(string Path, string Sha256, string Runtime, string RuntimeVersion,
    int MappedFunctions);

internal sealed record SiteCoverageBrowserSession(string Id, string Version, string MetadataPath, string MetadataSha256,
    IReadOnlyList<string> Origins, IReadOnlyList<string> ReceiptPaths);

internal sealed class SiteCoverageCollection
{
    public Dictionary<string, List<SiteCoverageScriptSnapshot>> SnapshotsBySource { get; } = new(StringComparer.Ordinal);
    public List<SiteCoverageReceipt> Receipts { get; } = [];
    public List<SiteCoverageBrowserSession> BrowserSessions { get; } = [];
    public SortedSet<string> Errors { get; } = new(StringComparer.Ordinal);
    public long BytesRead { get; set; }
    public int FilesRead { get; set; }
}

internal static class SiteCoverageArtifactReader
{
    public static async Task<SiteCoverageCollection> ReadAsync(string repository, string artifactRoot,
        SiteCoverageSourceManifest manifest, CancellationToken cancellationToken)
    {
        var result = new SiteCoverageCollection();
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
            result.Errors.Add(SiteCoverageTokens.MissingNativeReceiptsFailure);
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

            await ReadNativeFileAsync(file, repository, sources, null, SiteCoverageTokens.NodeRuntime, version,
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
            var artifactRoot = SiteCoverageArtifactFiles.RequiredCoverageRoot();
            var relativeMetadata = SiteCoverageArtifactFiles.RelativePath(artifactRoot, metadataPath);
            var receiptPaths = new List<string>();
            foreach (var name in metadata.CoverageFiles.Order(StringComparer.Ordinal))
            {
                var coveragePath = Path.Combine(directory, name);
                await ReadNativeFileAsync(coveragePath, repository, sources, metadata.Origins,
                    SiteCoverageTokens.BrowserRuntime, metadata.BrowserVersion, result, cancellationToken)
                    .ConfigureAwait(false);
                receiptPaths.Add(SiteCoverageArtifactFiles.RelativePath(artifactRoot, coveragePath));
            }

            result.BrowserSessions.Add(new(id, metadata.BrowserVersion, relativeMetadata,
                SiteCoverageSourceManifestWriter.Hash(metadataBytes), metadata.Origins.ToArray(), receiptPaths));
        }
        catch (Exception exception) when (SiteCoverageArtifactFiles.IsCoverageReadFailure(exception))
        {
            result.Errors.Add($"{SiteCoverageArtifactFiles.RelativePath(SiteCoverageArtifactFiles.RequiredCoverageRoot(), metadataPath)}{SiteCoverageTokens.ErrorSeparator}{exception.Message}");
        }
    }

    private static async Task ReadNativeFileAsync(string path, string repository,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources, IReadOnlySet<string>? origins,
        string runtime, string version, SiteCoverageCollection result, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await SiteCoverageArtifactFiles.ReadBoundedAsync(path, result, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes, SiteCoverageTokens.JsonDocumentOptions);
            SiteCoverageArtifactMetadata.ValidateEnvelope(document.RootElement, runtime);
            var artifactRoot = Environment.GetEnvironmentVariable(SiteCoverageTokens.CoverageRootEnvironment)!;
            var receiptIdentity = SiteCoverageArtifactFiles.RelativePath(artifactRoot, path);
            var receiptHash = SiteCoverageSourceManifestWriter.Hash(bytes);
            var snapshots = SiteCoverageNativeRanges.ParseScripts(document.RootElement.GetProperty(SiteCoverageTokens.Result),
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
            var artifactRoot = Environment.GetEnvironmentVariable(SiteCoverageTokens.CoverageRootEnvironment)!;
            result.Errors.Add($"{SiteCoverageArtifactFiles.RelativePath(artifactRoot, path)}{SiteCoverageTokens.ErrorSeparator}{exception.Message}");
        }
    }

}
