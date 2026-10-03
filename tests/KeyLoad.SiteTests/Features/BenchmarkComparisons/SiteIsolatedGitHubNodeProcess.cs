using System.Diagnostics;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedGitHubNodeProcess
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<JsonElement> RunAsync(string repository, object request, CancellationToken token,
        bool captureCoverage = true)
    {
        await using var temporary = SiteTempDirectory.Create();
        var script = Path.Combine(temporary.Path, SiteIsolatedGitHubTokens.Probe);
        var requestPath = Path.Combine(temporary.Path, SiteIsolatedGitHubTokens.Request);
        await File.WriteAllTextAsync(script, SiteIsolatedGitHubNodeProgram.Source, token);
        await File.WriteAllBytesAsync(requestPath, JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions), token);
        var result = await SiteIsolatedGitHubNativeProcess.RunProbeAsync(CreateStart(repository, script, requestPath, captureCoverage), token);
        if (result.ExitCode != 0 || result.StandardError.Length != 0)
        {
            throw new InvalidOperationException(SiteIsolatedGitHubTokens.NodeFailure);
        }

        using var document = JsonDocument.Parse(result.StandardOutput);
        return document.RootElement.Clone();
    }

    private static ProcessStartInfo CreateStart(string repository, string script, string request, bool captureCoverage)
    {
        var start = CreateStart(repository);
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(request);
        if (!captureCoverage)
        {
            start.Environment.Remove(SiteCoverageTokens.NodeCoverageEnvironment);
        }

        return start;
    }

    public static ProcessStartInfo CreateStart(string repository)
        => new(SiteTokens.NodeExecutable)
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
}
