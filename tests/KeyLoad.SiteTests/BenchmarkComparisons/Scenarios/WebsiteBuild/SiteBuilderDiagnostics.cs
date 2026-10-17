using System.Security.Cryptography;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBuilderDiagnostics
{
    internal static string GetEvidenceRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(SiteCoverageTokens.CoverageRootEnvironment);
        if (string.IsNullOrWhiteSpace(configuredRoot) || !Path.IsPathFullyQualified(configuredRoot))
        {
            throw new InvalidOperationException(SiteBuilderTokens.CoverageRootMissing);
        }

        return Directory.GetParent(Path.GetFullPath(configuredRoot))?.FullName is { } evidenceRoot
            ? Path.Combine(evidenceRoot, SiteBuilderTokens.EvidenceDirectoryName)
            : throw new InvalidOperationException(SiteBuilderTokens.CoverageRootMissing);
    }

    internal static async Task<SiteBuilderSourceReceipt[]> ReadBuilderSourcesAsync(SiteTestInputs inputs,
        CancellationToken cancellationToken)
    {
        var sources = new[] { SiteBuilderTokens.EntryBuilderPath, SiteBuilderTokens.FeatureBuilderPath };
        var receipts = new SiteBuilderSourceReceipt[sources.Length];
        for (var index = SiteTokens.Zero; index < sources.Length; index++)
        {
            var path = sources[index];
            var fullPath = Path.Combine(inputs.Repository,
                path.Replace(SiteTokens.UrlPathSeparatorCharacter, Path.DirectorySeparatorChar));
            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            receipts[index] = new(path, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }

        return receipts;
    }

    internal static string PreChromeFailure(SiteProcessResult result)
        => $"{SiteBuilderTokens.BuilderFailurePrefix} {result.ExitCode}{SiteBuilderTokens.ErrorSeparator}{result.StandardError}";
}

internal sealed record SiteBuilderSourceReceipt(string Path, string Sha256);
