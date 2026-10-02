using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteBuilderDiagnosticsTests
{
    [Test]
    public async Task AC_BC_017_RealBuilderSuccessRetainsBoundedOutputAndExactProvenance()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        var temporary = SiteTempDirectory.Create();
        SiteProcessResult result;
        try
        {
            result = await SiteBuilderProcess.RunAsync(inputs, inputs.Reports, temporary.Output, token);
        }
        finally
        {
            await temporary.DisposeAsync();
        }

        await Assert.That(Directory.Exists(temporary.Path)).IsFalse();
        await Assert.That(result.ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
        await Assert.That(result.StandardError.Length).IsEqualTo(SiteTokens.Zero);
        using var buildOutput = JsonDocument.Parse(result.StandardOutput);
        await Assert.That(buildOutput.RootElement.GetProperty(SiteAssetTokens.Profiles).GetInt32())
            .IsEqualTo(SiteTokens.ProfileNames.Length);
        var receipt = await ReadReceipt(temporary.Output, token);
        using (receipt.Document)
        {
            await AssertReceiptProvenance(inputs, receipt.Document, temporary.Output, null, token);
            await Assert.That(receipt.Document.RootElement.GetProperty(SiteBuilderTokens.ExitCode).GetInt32()).IsEqualTo(result.ExitCode);
        }
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(receipt.Directory, SiteBuilderTokens.StandardOutputFile), token))
            .IsEqualTo(result.StandardOutput);
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(receipt.Directory, SiteBuilderTokens.StandardErrorFile), token))
            .IsEqualTo(result.StandardError);
    }

    [Test]
    public async Task AC_BC_017_RealBuilderFailureRetainsActualErrorAndExitStatus()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        var temporary = SiteTempDirectory.Create();
        SiteProcessResult result;
        try
        {
            result = await SiteBuilderProcess.RunAsync(inputs, inputs.Reports, temporary.Output, token,
                SiteBuilderTokens.UnknownArgument);
        }
        finally
        {
            await temporary.DisposeAsync();
        }

        await Assert.That(result.ExitCode != SiteTokens.ProcessSuccessExitCode).IsTrue();
        await Assert.That(result.StandardError.Contains(SiteBuilderTokens.ArgumentError, StringComparison.Ordinal)).IsTrue();
        var startupFailure = SiteBuilderDiagnostics.PreChromeFailure(result);
        await Assert.That(startupFailure.StartsWith(SiteBuilderTokens.BuilderFailurePrefix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(startupFailure.Contains(result.ExitCode.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(startupFailure.Contains(result.StandardError, StringComparison.Ordinal)).IsTrue();
        await Assert.That(Directory.Exists(temporary.Path)).IsFalse();
        var receipt = await ReadReceipt(temporary.Output, token);
        using (receipt.Document)
        {
            await AssertReceiptProvenance(inputs, receipt.Document, temporary.Output,
                SiteBuilderTokens.UnknownArgument, token);
            await Assert.That(receipt.Document.RootElement.GetProperty(SiteBuilderTokens.ExitCode).GetInt32()).IsEqualTo(result.ExitCode);
        }
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(receipt.Directory, SiteBuilderTokens.StandardErrorFile), token))
            .IsEqualTo(result.StandardError);
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(receipt.Directory, SiteBuilderTokens.StandardOutputFile), token))
            .IsEqualTo(result.StandardOutput);
    }

    private static async Task<(JsonDocument Document, string Directory)> ReadReceipt(string output, CancellationToken token)
    {
        var root = SiteBuilderDiagnostics.GetEvidenceRoot();
        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException(SiteBuilderTokens.ReceiptMissing);
        }

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var path = Path.Combine(directory, SiteBuilderTokens.InvocationFile);
            if (!File.Exists(path))
            {
                continue;
            }

            var document = JsonDocument.Parse(await File.ReadAllBytesAsync(path, token));
            if (document.RootElement.GetProperty(SiteBuilderTokens.Arguments).EnumerateArray()
                .Any(argument => argument.GetString() == SiteTokens.OutputArgument + SiteBuilderTokens.ArgumentEquals + output))
            {
                return (document, directory);
            }

            document.Dispose();
        }

        throw new InvalidOperationException(SiteBuilderTokens.ReceiptMissing);
    }

    private static async Task AssertReceiptProvenance(SiteTestInputs inputs, JsonDocument receipt, string output,
        string? additionalArgument, CancellationToken token)
    {
        var root = receipt.RootElement;
        await AssertReceiptSchema(root);
        await AssertReceiptIdentity(inputs, root, output, additionalArgument);
        await AssertReceiptSources(inputs, root, token);
        await AssertReceiptFiles(root);
    }

    private static async Task AssertReceiptSchema(JsonElement root)
    {
        string[] expectedFields =
        [
            SiteBuilderTokens.SchemaVersion, SiteBuilderTokens.SiteSourceRevision, SiteBuilderTokens.MeasuredSourceRevision,
            SiteBuilderTokens.EvidenceRun, SiteBuilderTokens.EvidenceUrl, SiteBuilderTokens.WorkingDirectory,
            SiteBuilderTokens.Arguments, SiteBuilderTokens.BuilderSources, SiteBuilderTokens.ExitCode,
            SiteBuilderTokens.StandardOutput, SiteBuilderTokens.StandardError,
        ];
        var fields = root.EnumerateObject().Select(property => property.Name).ToArray();
        await Assert.That(fields.SequenceEqual(expectedFields, StringComparer.Ordinal)).IsTrue();
        await Assert.That(root.GetProperty(SiteBuilderTokens.SchemaVersion).GetInt32())
            .IsEqualTo(SiteBuilderTokens.ReceiptSchemaVersion);
    }

    private static async Task AssertReceiptIdentity(SiteTestInputs inputs, JsonElement root, string output,
        string? additionalArgument)
    {
        await Assert.That(root.GetProperty(SiteBuilderTokens.SiteSourceRevision).GetString()).IsEqualTo(inputs.SiteRevision);
        await Assert.That(root.GetProperty(SiteBuilderTokens.MeasuredSourceRevision).GetString()).IsEqualTo(inputs.MeasuredRevision);
        await Assert.That(root.GetProperty(SiteBuilderTokens.EvidenceRun).GetString()).IsEqualTo(inputs.EvidenceRun);
        await Assert.That(root.GetProperty(SiteBuilderTokens.EvidenceUrl).GetString()).IsEqualTo(inputs.EvidenceUrl);
        await Assert.That(root.GetProperty(SiteBuilderTokens.WorkingDirectory).GetString()).IsEqualTo(inputs.Repository);
        var arguments = root.GetProperty(SiteBuilderTokens.Arguments).EnumerateArray()
            .Select(argument => argument.GetString() ?? string.Empty).ToArray();
        var expectedArguments = new List<string>
        {
            Path.Combine(inputs.Repository, SiteBuilderTokens.EntryBuilderPath),
            SiteTokens.ReportsArgument + SiteBuilderTokens.ArgumentEquals + inputs.Reports,
            SiteTokens.OutputArgument + SiteBuilderTokens.ArgumentEquals + output,
            SiteTokens.RevisionArgument + SiteBuilderTokens.ArgumentEquals + inputs.MeasuredRevision,
            SiteTokens.EvidenceArgument + SiteBuilderTokens.ArgumentEquals + inputs.EvidenceUrl,
            SiteTokens.SiteRevisionArgument + SiteBuilderTokens.ArgumentEquals + inputs.SiteRevision,
        };
        if (additionalArgument is not null)
        {
            expectedArguments.Add(additionalArgument);
        }

        await Assert.That(arguments.SequenceEqual(expectedArguments, StringComparer.Ordinal)).IsTrue();
    }

    private static async Task AssertReceiptSources(SiteTestInputs inputs, JsonElement root, CancellationToken token)
    {
        var sources = root.GetProperty(SiteBuilderTokens.BuilderSources).EnumerateArray().ToArray();
        await Assert.That(sources.Length).IsEqualTo(SiteBuilderTokens.ExpectedBuilderSourceCount);
        await AssertBuilderSourceHash(inputs, FindBuilderSource(sources, SiteBuilderTokens.EntryBuilderPath),
            SiteBuilderTokens.EntryBuilderPath, token);
        await AssertBuilderSourceHash(inputs, FindBuilderSource(sources, SiteBuilderTokens.FeatureBuilderPath),
            SiteBuilderTokens.FeatureBuilderPath, token);
    }

    private static async Task AssertReceiptFiles(JsonElement root)
    {
        await Assert.That(root.GetProperty(SiteBuilderTokens.StandardOutput).GetString())
            .IsEqualTo(SiteBuilderTokens.StandardOutputFile);
        await Assert.That(root.GetProperty(SiteBuilderTokens.StandardError).GetString())
            .IsEqualTo(SiteBuilderTokens.StandardErrorFile);
    }

    private static async Task AssertBuilderSourceHash(SiteTestInputs inputs, JsonElement source, string relativePath,
        CancellationToken token)
    {
        string[] expectedProperties = [SiteBuilderTokens.Path, SiteBuilderTokens.Sha256];
        var properties = source.EnumerateObject().Select(property => property.Name).ToArray();
        await Assert.That(properties.SequenceEqual(expectedProperties, StringComparer.Ordinal)).IsTrue();
        await Assert.That(source.GetProperty(SiteBuilderTokens.Path).GetString()).IsEqualTo(relativePath);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(inputs.Repository,
            relativePath.Replace(SiteTokens.UrlPathSeparatorCharacter, Path.DirectorySeparatorChar)), token);
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await Assert.That(source.GetProperty(SiteBuilderTokens.Sha256).GetString()).IsEqualTo(expectedHash);
    }

    private static JsonElement FindBuilderSource(JsonElement[] sources, string relativePath)
        => sources.Single(source => source.GetProperty(SiteBuilderTokens.Path).GetString() == relativePath);
}
