using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBuilderDiagnostics
{
    private static readonly JsonSerializerOptions ReceiptOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

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

    internal static async Task RetainAsync(SiteTestInputs inputs, string[] arguments,
        SiteBuilderSourceReceipt[] builderSources, SiteProcessResult result, CancellationToken cancellationToken)
    {
        var evidenceRoot = GetEvidenceRoot();
        Directory.CreateDirectory(evidenceRoot);
        var invocationDirectory = Path.Combine(evidenceRoot, Guid.NewGuid().ToString(SiteBuilderTokens.GuidFormat));
        Directory.CreateDirectory(invocationDirectory);
        await File.WriteAllTextAsync(Path.Combine(invocationDirectory, SiteBuilderTokens.StandardOutputFile),
            result.StandardOutput, Utf8WithoutBom, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(invocationDirectory, SiteBuilderTokens.StandardErrorFile),
            result.StandardError, Utf8WithoutBom, cancellationToken);
        var receipt = new SiteBuilderInvocationReceipt(SiteBuilderTokens.ReceiptSchemaVersion, inputs.SiteRevision,
            inputs.MeasuredRevision, inputs.EvidenceRun, inputs.EvidenceUrl, inputs.Repository, arguments,
            builderSources, result.ExitCode, SiteBuilderTokens.StandardOutputFile, SiteBuilderTokens.StandardErrorFile);
        var receiptBytes = JsonSerializer.SerializeToUtf8Bytes(receipt, ReceiptOptions);
        var stagingPath = Path.Combine(invocationDirectory, SiteBuilderTokens.InvocationStagingFile);
        await File.WriteAllBytesAsync(stagingPath, receiptBytes, cancellationToken);
        File.Move(stagingPath, Path.Combine(invocationDirectory, SiteBuilderTokens.InvocationFile));
    }

    internal static string PreChromeFailure(SiteProcessResult result)
        => $"{SiteBuilderTokens.BuilderFailurePrefix} {result.ExitCode}{SiteBuilderTokens.ErrorSeparator}{result.StandardError}";
}

internal sealed record SiteBuilderSourceReceipt(string Path, string Sha256);

internal sealed record SiteBuilderInvocationReceipt(int SchemaVersion, string SiteSourceRevision,
    string MeasuredSourceRevision, string EvidenceRun, string EvidenceUrl, string WorkingDirectory,
    string[] Arguments, SiteBuilderSourceReceipt[] BuilderSources, int ExitCode, string StdoutFile,
    string StderrFile);
