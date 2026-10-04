using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteIsolatedGitHubInputs(string Capture, string Receipt, JsonObject Metadata)
{
    public static async Task<SiteIsolatedGitHubInputs> ReadAsync(CancellationToken token)
    {
        var capture = Required(SiteIsolatedGitHubTokens.CaptureEnvironment);
        var receipt = Required(SiteIsolatedGitHubTokens.ReceiptEnvironment);
        var metadata = JsonNode.Parse(await File.ReadAllBytesAsync(Path.Combine(capture,
            SiteIsolatedGitHubTokens.Metadata), token))!.AsObject();
        return new(capture, receipt, metadata);
    }

    public static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            throw new InvalidOperationException(SiteIsolatedGitHubTokens.Missing);
        }

        return Path.GetFullPath(value);
    }
}
