using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteCoverageSourceEntry(string Path, string Sha256, int Utf16Length);

internal sealed record SiteCoverageSourceManifest(int SchemaVersion, string SourceRevision, string NodeVersion,
    IReadOnlyList<SiteCoverageSourceEntry> Sources);

internal static class SiteCoverageSourceManifestWriter
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static string? baselineManifestHash;

    public static async Task CaptureAsync()
    {
        var repository = RequiredPath(SiteTokens.RepositoryEnvironment);
        var artifactRoot = RequiredPath(SiteCoverageTokens.CoverageRootEnvironment);
        var nodeCoverage = RequiredPath(SiteCoverageTokens.NodeCoverageEnvironment);
        var revision = Environment.GetEnvironmentVariable(SitePublicationTokens.SourceRevisionEnvironment);
        if (!Directory.Exists(repository) || !Directory.Exists(artifactRoot) || !IsRevision(revision) ||
            !PathsEqual(nodeCoverage, Path.Combine(artifactRoot, SiteCoverageTokens.NodeDirectory)))
        {
            throw new InvalidOperationException(SiteCoverageTokens.InvalidRootFailure);
        }

        var manifestPath = Path.Combine(artifactRoot, SiteCoverageTokens.SourceManifestFile);
        var reportPath = Path.Combine(artifactRoot, SiteCoverageTokens.ReportFile);
        var pendingPath = reportPath + SiteCoverageTokens.TemporaryReportSuffix;
        if ((File.GetAttributes(artifactRoot) & FileAttributes.ReparsePoint) != 0 ||
            File.Exists(manifestPath) || File.Exists(reportPath) || File.Exists(pendingPath))
        {
            throw new InvalidOperationException(SiteCoverageTokens.InvalidRootFailure);
        }

        var sources = await CaptureSourcesAsync(repository, SiteCoverageModeSelector.Current).ConfigureAwait(false);
        RejectStaleReceipts(artifactRoot);
        var nodeVersion = await ReadNodeVersionAsync(repository).ConfigureAwait(false);
        var manifest = new SiteCoverageSourceManifest(SiteCoverageTokens.Schema, revision!, nodeVersion, sources);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, SiteCoverageTokens.JsonOptions);
        await WriteNewFileAsync(manifestPath, bytes).ConfigureAwait(false);
        baselineManifestHash = Hash(bytes);
    }

    public static async Task<string> VerifyManifestAsync(string artifactRoot)
    {
        var path = Path.Combine(artifactRoot, SiteCoverageTokens.SourceManifestFile);
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        var hash = Hash(bytes);
        if (baselineManifestHash is null || baselineManifestHash != hash)
        {
            throw new InvalidOperationException(SiteCoverageTokens.SourceMismatchFailure);
        }

        return hash;
    }

    public static async Task VerifyUnchangedAsync(string repository, SiteCoverageSourceManifest manifest)
    {
        foreach (var expected in manifest.Sources)
        {
            var path = ResolveSourcePath(repository, expected.Path);
            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            var source = StrictUtf8.GetString(bytes);
            if (Hash(bytes) != expected.Sha256 || source.Length != expected.Utf16Length)
            {
                throw new InvalidOperationException(SiteCoverageTokens.SourceMismatchFailure);
            }
        }
    }

    private static async Task<IReadOnlyList<SiteCoverageSourceEntry>> CaptureSourcesAsync(string repository,
        SiteCoverageMode mode)
    {
        var inventory = SiteCoverageSourceInventory.ProductionSources.Order(StringComparer.Ordinal).ToArray();
        var expected = SiteCoverageModeSelector.Sources(mode).Order(StringComparer.Ordinal).ToArray();
        var actualFeature = SiteCoverageSourcePaths.EnumerateModules(repository,
            SiteCoverageTokens.FeatureSourcePrefix).Order(StringComparer.Ordinal).ToArray();
        var expectedFeature = inventory.Where(path => path.StartsWith(SiteCoverageTokens.FeatureSourcePrefix, StringComparison.Ordinal))
            .ToArray();
        var actualTools = SiteCoverageSourcePaths.EnumerateModules(repository,
                SitePublicationTokens.EvidenceToolsPrefix)
            .Where(SiteCoverageSourceInventory.IsTrackedEvidenceModule)
            .Order(StringComparer.Ordinal).ToArray();
        var expectedTools = inventory.Where(path => path.StartsWith(SitePublicationTokens.EvidenceToolsPrefix,
                StringComparison.Ordinal) && SiteCoverageSourceInventory.IsTrackedEvidenceModule(path))
            .ToArray();
        if (!actualFeature.SequenceEqual(expectedFeature, StringComparer.Ordinal) ||
            !actualTools.SequenceEqual(expectedTools, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(SiteCoverageTokens.InventoryFailure);
        }

        var sources = new List<SiteCoverageSourceEntry>(expected.Length);
        foreach (var relativePath in expected)
        {
            var fullPath = ResolveSourcePath(repository, relativePath);
            var bytes = await File.ReadAllBytesAsync(fullPath).ConfigureAwait(false);
            var source = StrictUtf8.GetString(bytes);
            if (source.Length > SiteCoverageTokens.MaximumUtf16SourceLength)
            {
                throw new InvalidOperationException(SiteCoverageTokens.InvalidSourceFailure);
            }

            sources.Add(new(relativePath, Hash(bytes), source.Length));
        }

        return sources;
    }

    private static void RejectStaleReceipts(string artifactRoot)
    {
        var nodeRoot = Path.Combine(artifactRoot, SiteCoverageTokens.NodeDirectory);
        var browserRoot = Path.Combine(artifactRoot, SiteCoverageTokens.BrowserDirectory);
        var sessionsRoot = Path.Combine(browserRoot, SiteCoverageTokens.SessionsDirectory);
        if (!Directory.Exists(nodeRoot))
        {
            Directory.CreateDirectory(nodeRoot);
        }

        if ((File.GetAttributes(nodeRoot) & FileAttributes.ReparsePoint) != 0 ||
            Directory.Exists(browserRoot) && (File.GetAttributes(browserRoot) & FileAttributes.ReparsePoint) != 0 ||
            Directory.Exists(sessionsRoot) && ((File.GetAttributes(sessionsRoot) & FileAttributes.ReparsePoint) != 0 ||
                Directory.EnumerateFileSystemEntries(sessionsRoot).Any()) ||
            Directory.EnumerateFileSystemEntries(nodeRoot).Any())
        {
            throw new InvalidOperationException(SiteCoverageTokens.StaleReceiptFailure);
        }
    }

    private static string ResolveSourcePath(string repository, string relativePath)
    {
        return SiteCoverageSourcePaths.ResolveSourcePath(repository, relativePath);
    }

    private static async Task<string> ReadNodeVersionAsync(string repository)
    {
        var startInfo = new ProcessStartInfo(SiteTokens.NodeExecutable)
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment.Remove(SiteCoverageTokens.NodeCoverageEnvironment);
        startInfo.ArgumentList.Add(SiteCoverageTokens.NodeVersionArgument);
        var result = await SiteCoverageNodeProcess.RunAsync(startInfo, CancellationToken.None).ConfigureAwait(false);
        var output = result.StandardOutput.Trim();
        if (result.ExitCode != SiteTokens.ProcessSuccessExitCode || result.StandardError.Trim().Length != SiteTokens.Zero ||
            !output.StartsWith(SiteCoverageTokens.NodeVersionPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(SiteCoverageTokens.NodeVersionFailure);
        }

        return output;
    }

    private static async Task WriteNewFileAsync(string path, byte[] bytes)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(bytes).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    internal static bool IsRevision(string? value) => value is { Length: SiteCoverageTokens.RevisionLength } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool IsHash(string? value) => value is { Length: SiteCoverageTokens.ShaLength } &&
        value.All(IsHashDigit);

    private static bool IsHashDigit(char character) => character is >= '0' and <= '9' or >= 'a' and <= 'f';

    internal static bool PathsEqual(string left, string right) => SiteCoverageSourcePaths.PathsEqual(left, right);

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string RequiredPath(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (value is null || !Path.IsPathFullyQualified(value))
        {
            throw new InvalidOperationException(SiteCoverageTokens.InvalidRootFailure);
        }

        return Path.GetFullPath(value);
    }

}
