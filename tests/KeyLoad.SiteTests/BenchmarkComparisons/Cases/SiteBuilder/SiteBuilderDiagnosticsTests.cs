using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteBuilderDiagnosticsTests
{
    [Test]
    public async Task CurrentBuilderRetainsBoundedOutputAndExactCohortSourceReceipt()
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var inputs = fixture.Inputs.Site;
        var token = TestContext.Current!.Execution.CancellationToken;
        var temporary = SiteTempDirectory.Create();
        SiteProcessResult result;
        try
        {
            result = await SiteIsolatedBuilderProcess.RunAsync(fixture, temporary.Output, token);
        }
        finally
        {
            await temporary.DisposeAsync();
        }

        await Assert.That(Directory.Exists(temporary.Path)).IsFalse();
        await Assert.That(result.ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
        await Assert.That(result.StandardError).IsEqualTo(string.Empty);
        using var output = JsonDocument.Parse(result.StandardOutput);
        await Assert.That(output.RootElement.GetProperty(SiteBuilderTokens.Output).GetString()).IsEqualTo(temporary.Output);
        await Assert.That(output.RootElement.GetProperty(SiteBuilderTokens.JavascriptGzipBytes).GetInt32())
            .IsLessThanOrEqualTo(SiteBuilderTokens.AuthoredJavaScriptGzipLimit);
        await Assert.That(output.RootElement.GetProperty(SiteBuilderTokens.CssGzipBytes).GetInt32())
            .IsLessThanOrEqualTo(SiteBuilderTokens.AuthoredCssGzipLimit);
        var receipt = await ReadReceipt(temporary.Output, token);
        using (receipt.Document)
        {
            await AssertReceiptProvenance(inputs, receipt.Document, temporary.Output, null, token);
            await Assert.That(receipt.Document.RootElement.GetProperty(SiteBuilderTokens.ExitCode).GetInt32())
                .IsEqualTo(result.ExitCode);
        }
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(receipt.Directory,
            SiteBuilderTokens.StandardOutputFile), token)).IsEqualTo(result.StandardOutput);
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(receipt.Directory,
            SiteBuilderTokens.StandardErrorFile), token)).IsEqualTo(result.StandardError);
    }

    [Test]
    public async Task CurrentBuilderFailureRetainsActualErrorExitAndCohortReceipt()
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var inputs = fixture.Inputs.Site;
        var token = TestContext.Current!.Execution.CancellationToken;
        var temporary = SiteTempDirectory.Create();
        SiteProcessResult result;
        try
        {
            result = await SiteIsolatedBuilderProcess.RunAsync(fixture, temporary.Output, token,
                additionalArgument: SiteBuilderTokens.UnknownArgument);
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
            await Assert.That(receipt.Document.RootElement.GetProperty(SiteBuilderTokens.ExitCode).GetInt32())
                .IsEqualTo(result.ExitCode);
        }
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(receipt.Directory,
            SiteBuilderTokens.StandardErrorFile), token)).IsEqualTo(result.StandardError);
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(receipt.Directory,
            SiteBuilderTokens.StandardOutputFile), token)).IsEqualTo(result.StandardOutput);
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
                .Any(argument => argument.GetString() == SiteBuilderTokens.OutputArgument + output))
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
        string[] expectedFields = [SiteBuilderTokens.SchemaVersion, SiteBuilderTokens.SiteSourceRevision,
            SiteBuilderTokens.Cohort, SiteBuilderTokens.Arguments, SiteBuilderTokens.Sources,
            SiteBuilderTokens.ExitCode, SiteBuilderTokens.Stdout, SiteBuilderTokens.Stderr];
        await Assert.That(root.EnumerateObject().Select(property => property.Name)
            .SequenceEqual(expectedFields, StringComparer.Ordinal)).IsTrue();
        await Assert.That(root.GetProperty(SiteBuilderTokens.SchemaVersion).GetInt32())
            .IsEqualTo(SiteBuilderTokens.ReceiptSchemaVersion);
        await Assert.That(root.GetProperty(SiteBuilderTokens.SiteSourceRevision).GetString()).IsEqualTo(inputs.SiteRevision);
        var cohort = root.GetProperty(SiteBuilderTokens.Cohort);
        await Assert.That(cohort.GetProperty(SiteIsolatedFields.SourceRevision).GetString()).IsEqualTo(inputs.MeasuredRevision);
        await Assert.That(cohort.GetProperty(SiteIsolatedFields.RunId).GetInt64().ToString(CultureInfo.InvariantCulture))
            .IsEqualTo(inputs.EvidenceRun);
        await Assert.That(cohort.GetProperty(SiteIsolatedFields.Attempt).GetInt32()).IsGreaterThan(0);

        var arguments = root.GetProperty(SiteBuilderTokens.Arguments).EnumerateArray()
            .Select(argument => argument.GetString() ?? string.Empty).ToArray();
        var expectedArguments = new List<string>
        {
            Path.Combine(inputs.Repository, SiteBuilderTokens.EntryBuilderPath),
            SiteBuilderTokens.IsolatedArgument + inputs.Aggregate,
            SiteBuilderTokens.OutputArgument + output,
            SiteBuilderTokens.RevisionArgument + inputs.MeasuredRevision,
            SiteBuilderTokens.EvidenceArgument + inputs.EvidenceUrl,
            SiteBuilderTokens.SiteRevisionArgument + inputs.SiteRevision,
        };
        if (additionalArgument is not null)
        {
            expectedArguments.Add(additionalArgument);
        }
        await Assert.That(arguments.SequenceEqual(expectedArguments, StringComparer.Ordinal)).IsTrue();

        var sources = root.GetProperty(SiteBuilderTokens.Sources).EnumerateArray().ToArray();
        await Assert.That(sources.Length).IsEqualTo(SiteBuilderTokens.ExpectedBuilderSourceCount);
        foreach (var source in sources)
        {
            var relativePath = source.GetProperty(SiteBuilderTokens.Path).GetString()!;
            var bytes = await File.ReadAllBytesAsync(Path.Combine(inputs.Repository,
                relativePath.Replace('/', Path.DirectorySeparatorChar)), token);
            await Assert.That(source.GetProperty(SiteBuilderTokens.Sha256).GetString())
                .IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        await Assert.That(root.GetProperty(SiteBuilderTokens.Stdout).GetString()).IsEqualTo(SiteBuilderTokens.StandardOutputFile);
        await Assert.That(root.GetProperty(SiteBuilderTokens.Stderr).GetString()).IsEqualTo(SiteBuilderTokens.StandardErrorFile);
    }
}
