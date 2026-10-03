using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedGitHubScope : IAsyncDisposable
{
    private readonly SiteTempDirectory _temporary;

    private SiteIsolatedGitHubScope(SiteIsolatedGitHubInputs inputs)
    {
        Inputs = inputs;
        _temporary = SiteTempDirectory.Create();
    }

    public SiteIsolatedGitHubInputs Inputs { get; }
    public string Capture => Path.Combine(_temporary.Path, SiteIsolatedGitHubFields.Capture);
    public string Before => Path.Combine(_temporary.Path, SiteIsolatedGitHubFields.Before);
    public string After => Path.Combine(_temporary.Path, SiteIsolatedGitHubFields.After);

    public static async Task<SiteIsolatedGitHubScope> CreateAsync(CancellationToken token)
    {
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        var scope = new SiteIsolatedGitHubScope(inputs);
        try
        {
            Directory.CreateDirectory(scope.Capture);
            await CopyMetadataAsync(Path.Combine(inputs.Capture, SiteIsolatedGitHubFields.MetadataDirectory),
                Path.Combine(scope.Capture, SiteIsolatedGitHubFields.MetadataDirectory), token);
            return scope;
        }
        catch (Exception)
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    private static async Task CopyMetadataAsync(string source, string target, CancellationToken token)
    {
        Directory.CreateDirectory(target);
        foreach (var path in Directory.EnumerateFileSystemEntries(source))
        {
            token.ThrowIfCancellationRequested();
            var destination = Path.Combine(target, Path.GetFileName(path));
            if (Directory.Exists(path))
            {
                if (new DirectoryInfo(path).LinkTarget is not null)
                {
                    throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidPath);
                }

                await CopyMetadataAsync(path, destination, token);
            }
            else
            {
                SiteIsolatedGitHubFileOperations.RequireRegular(path);
                File.Copy(path, destination);
                File.SetAttributes(destination, FileAttributes.Normal);
            }
        }
    }

    public string SelectedJobsPath => Path.Combine(Capture, SiteIsolatedGitHubFields.MetadataDirectory,
        SiteIsolatedGitHubFields.Attempts, RunValue(SiteIsolatedGitHubTokens.Id),
        RunValue(SiteIsolatedGitHubTokens.Attempt), SiteIsolatedGitHubFields.Jobs);

    public string ArtifactsPath => Path.Combine(Capture, SiteIsolatedGitHubFields.MetadataDirectory,
        SiteIsolatedGitHubFields.ArtifactPages);

    public async Task<JsonElement> SelectAsync(CancellationToken token) => await RunAsync(
        SiteIsolatedGitHubFields.SelectionOperation, Arguments(), token);

    public async Task<JsonElement> ProveAsync(CancellationToken token) => await RunAsync(
        SiteIsolatedGitHubFields.ProofOperation, Arguments(), token);

    private object Arguments() => new
    {
        input = Capture,
        mode = SiteIsolatedGitHubTokens.Validate,
        requestedRun = RunValue(SiteIsolatedGitHubTokens.Id),
        source = new
        {
            website = Inputs.Metadata[SiteIsolatedGitHubTokens.Source]![SiteIsolatedGitHubTokens.Website]!.GetValue<string>(),
            control = Inputs.Metadata[SiteIsolatedGitHubTokens.Source]![SiteIsolatedGitHubTokens.Control]!.GetValue<string>(),
        },
    };

    public static async Task<JsonElement> RunAsync(string operation, object arguments, CancellationToken token)
    {
        var repository = SiteTestInputs.Read().Repository;
        return await SiteIsolatedGitHubNodeProcess.RunAsync(repository, new { repository, operation, arguments }, token);
    }

    private string RunValue(string key) => Inputs.Metadata[SiteIsolatedGitHubTokens.Run]![key]!
        .GetValue<long>().ToString(CultureInfo.InvariantCulture);

    public static async Task MutateAsync(string path, Action<JsonArray> mutation, CancellationToken token)
    {
        var json = JsonNode.Parse(await File.ReadAllBytesAsync(path, token))!.AsArray();
        mutation(json);
        await File.WriteAllTextAsync(path, json.ToJsonString(), token);
    }

    public ValueTask DisposeAsync() => _temporary.DisposeAsync();
}
