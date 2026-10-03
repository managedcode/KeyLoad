using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedGitHubIntegrityScope : IAsyncDisposable
{
    private readonly SiteTempDirectory temporary;

    private SiteIsolatedGitHubIntegrityScope(SiteIsolatedGitHubArchiveReceipt original)
    {
        Original = original;
        temporary = SiteTempDirectory.Create();
    }

    public SiteIsolatedGitHubArchiveReceipt Original { get; }

    public SiteIsolatedGitHubArchiveReceipt PrivateReceipt { get; private set; } = null!;

    public static async Task<SiteIsolatedGitHubIntegrityScope> CreateAsync(CancellationToken token)
    {
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        var original = await ReadAuthorityAsync(inputs, token);
        var scope = new SiteIsolatedGitHubIntegrityScope(original);
        try
        {
            var capture = Path.Combine(scope.temporary.Path, SiteIsolatedGitHubFields.Capture);
            var result = await SiteIsolatedGitHubScope.RunAsync(SiteIsolatedGitHubFields.CloneOperation,
                new { input = inputs.Capture, output = capture }, token);
            if (!result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean() ||
                result.GetProperty(SiteIsolatedGitHubFields.Result).GetProperty(SiteIsolatedGitHubTokens.FileCountField).GetInt32() !=
                    SiteIsolatedGitHubTokens.FileCount)
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
            }

            scope.PrivateReceipt = new(capture, Path.Combine(capture, SiteIsolatedGitHubTokens.Receipt),
                original.Value, original.Files);
            return scope;
        }
        catch (Exception)
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    private static async Task<SiteIsolatedGitHubArchiveReceipt> ReadAuthorityAsync(SiteIsolatedGitHubInputs inputs,
        CancellationToken token)
    {
        var value = JsonNode.Parse(await File.ReadAllBytesAsync(inputs.Receipt, token))!.AsObject();
        var files = value[SiteIsolatedGitHubTokens.InputFiles]!.AsArray().Select(file =>
            new SiteIsolatedGitHubArchiveFile(file![SiteIsolatedGitHubTokens.Path]!.GetValue<string>(),
                file[SiteIsolatedGitHubTokens.Bytes]!.GetValue<long>(),
                file[SiteIsolatedGitHubTokens.Sha256]!.GetValue<string>())).ToArray();
        return new(inputs.Capture, inputs.Receipt, value, files);
    }

    public ValueTask DisposeAsync() => temporary.DisposeAsync();
}
