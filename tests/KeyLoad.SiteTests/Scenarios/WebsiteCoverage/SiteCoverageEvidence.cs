using System.Text;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteCoverageEvidence
{
    public static async Task CaptureAsync(SiteCoverageFixtureDirectory temporary, string sourcePath,
        RuntimeSiteCoverageReceipt receipt, CancellationToken cancellationToken)
    {
        var configuredRoot = Environment.GetEnvironmentVariable(SiteCoverageTokens.CoverageRootEnvironment);
        if (configuredRoot is null || !Path.IsPathFullyQualified(configuredRoot) ||
            SiteCoverageSourceManifestWriter.Hash(receipt.Bytes) != receipt.Sha256)
        {
            throw new InvalidOperationException(SiteCoverageTokens.InvalidRootFailure);
        }

        var evidenceRoot = Path.Combine(Path.GetFullPath(configuredRoot), SiteCoverageTokens.RuntimeEvidenceDirectory);
        Directory.CreateDirectory(evidenceRoot);
        var evidenceDirectory = Path.Combine(evidenceRoot, Guid.NewGuid().ToString(SiteCoverageTokens.CompactGuidFormat));
        Directory.CreateDirectory(evidenceDirectory);
        var sourceFile = Path.Combine(temporary.Root,
            sourcePath.Replace(SiteCoverageTokens.RelativeSeparator, Path.DirectorySeparatorChar));
        var sourceBytes = await File.ReadAllBytesAsync(sourceFile, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, SiteCoverageTokens.EvidenceSourceFile),
            sourceBytes, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, SiteCoverageTokens.EvidenceReceiptFile),
            receipt.Bytes, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, SiteCoverageTokens.EvidenceOutputFile),
            receipt.StandardOutput, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        var metadata = new SiteCoverageEvidenceMetadata(sourcePath,
            SiteCoverageSourceManifestWriter.Hash(sourceBytes), receipt.Identity, receipt.Sha256,
            SiteCoverageTokens.EvidenceSourceFile, SiteCoverageTokens.EvidenceReceiptFile,
            SiteCoverageTokens.EvidenceOutputFile);
        var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata, SiteCoverageTokens.JsonOptions);
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, SiteCoverageTokens.EvidenceMetadataFile),
            metadataBytes, cancellationToken).ConfigureAwait(false);
    }
}
