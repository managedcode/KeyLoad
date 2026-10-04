using System.Text;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteCoverageNativeEvidence
{
    public static async Task CaptureAsync(SiteCoverageFixtureDirectory temporary, string sourcePath,
        SiteCoverageNativeReceipt receipt, CancellationToken cancellationToken)
    {
        var configuredRoot = Environment.GetEnvironmentVariable(SiteCoverageTokens.CoverageRootEnvironment);
        if (configuredRoot is null || !Path.IsPathFullyQualified(configuredRoot) ||
            SiteCoverageSourceManifestWriter.Hash(receipt.Bytes) != receipt.Sha256)
        {
            throw new InvalidOperationException(SiteCoverageTokens.InvalidRootFailure);
        }

        var evidenceRoot = Path.Combine(Path.GetFullPath(configuredRoot), SiteCoverageTokens.NativeEvidenceDirectory);
        Directory.CreateDirectory(evidenceRoot);
        var evidenceDirectory = Path.Combine(evidenceRoot, Guid.NewGuid().ToString(SiteCoverageTokens.CompactGuidFormat));
        Directory.CreateDirectory(evidenceDirectory);
        var sourceFile = Path.Combine(temporary.Root,
            sourcePath.Replace(SiteCoverageTokens.RelativeSeparator, Path.DirectorySeparatorChar));
        var sourceBytes = await File.ReadAllBytesAsync(sourceFile, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, SiteCoverageTokens.NativeEvidenceSourceFile),
            sourceBytes, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, SiteCoverageTokens.NativeEvidenceReceiptFile),
            receipt.Bytes, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, SiteCoverageTokens.NativeEvidenceOutputFile),
            receipt.StandardOutput, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        var metadata = new SiteCoverageNativeEvidenceMetadata(sourcePath,
            SiteCoverageSourceManifestWriter.Hash(sourceBytes), receipt.Identity, receipt.Sha256,
            SiteCoverageTokens.NativeEvidenceSourceFile, SiteCoverageTokens.NativeEvidenceReceiptFile,
            SiteCoverageTokens.NativeEvidenceOutputFile);
        var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata, SiteCoverageTokens.JsonOptions);
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, SiteCoverageTokens.NativeEvidenceMetadataFile),
            metadataBytes, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed record SiteCoverageNativeEvidenceMetadata(string FixtureSourcePath, string FixtureSourceSha256,
    string ReceiptIdentity, string ReceiptSha256, string SourceFile, string NativeReceiptFile, string StandardOutputFile);
