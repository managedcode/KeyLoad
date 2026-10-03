using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedBuilderProcess
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<SiteProcessResult> RunAsync(SiteIsolatedFixture fixture, string output,
        CancellationToken token, string? input = null)
    {
        using var projection = JsonDocument.Parse(await File.ReadAllBytesAsync(fixture.Projection, token));
        var cohort = projection.RootElement.GetProperty(SiteIsolatedFields.Cohort).Clone();
        var start = new ProcessStartInfo(SiteTokens.NodeExecutable)
        {
            WorkingDirectory = fixture.Inputs.Site.Repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var arguments = new[]
        {
            Path.Combine(fixture.Inputs.Site.Repository, SiteAssetTokens.BuilderRelativePath),
            "--isolated=" + (input ?? fixture.Inputs.Aggregate), "--output=" + output,
            "--revision=" + cohort.GetProperty(SiteIsolatedFields.SourceRevision).GetString(),
            "--evidence-url=" + SiteTokens.EvidenceBase + cohort.GetProperty(SiteIsolatedFields.RunId).GetInt64().ToString(CultureInfo.InvariantCulture),
            "--site-revision=" + fixture.Inputs.Site.SiteRevision,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        var sources = await SiteBuilderDiagnostics.ReadBuilderSourcesAsync(fixture.Inputs.Site, token);
        var result = await SiteIsolatedNodeProcess.RunProcessAsync(start, token);
        await RetainAsync(fixture.Inputs.Site.SiteRevision, cohort, arguments, sources, result, token);
        return result;
    }

    private static async Task RetainAsync(string siteRevision, JsonElement cohort, string[] arguments,
        SiteBuilderSourceReceipt[] sources, SiteProcessResult result, CancellationToken token)
    {
        var directory = Path.Combine(SiteBuilderDiagnostics.GetEvidenceRoot(), "isolated-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "stdout.txt"), result.StandardOutput, token);
        await File.WriteAllTextAsync(Path.Combine(directory, "stderr.txt"), result.StandardError, token);
        var receipt = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            siteSourceRevision = siteRevision,
            cohort,
            arguments,
            sources,
            exitCode = result.ExitCode,
            stdout = "stdout.txt",
            stderr = "stderr.txt",
        }, JsonOptions);
        await File.WriteAllBytesAsync(Path.Combine(directory, "invocation.json"), receipt, token);
    }
}
