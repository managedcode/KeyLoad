using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteIsolatedGitHubArchiveFile(string Path, long Bytes, string Sha256);

internal sealed class SiteIsolatedGitHubArchiveReceipt(string capture, string receiptPath,
    JsonObject value, SiteIsolatedGitHubArchiveFile[] files)
{
    private JsonObject OriginalValue { get; } = value.DeepClone().AsObject();
    private SiteIsolatedGitHubArchiveFile[] OriginalFiles { get; } = [.. files];

    public string Capture { get; } = capture;

    public string ReceiptPath { get; } = receiptPath;

    public JsonObject Value => OriginalValue.DeepClone().AsObject();

    public SiteIsolatedGitHubArchiveFile[] Files => [.. OriginalFiles];

    internal bool Matches(JsonNode? value) => JsonNode.DeepEquals(OriginalValue, value);
}

internal static class SiteIsolatedGitHubReceiptReader
{
    public static async Task<JsonObject> ReadMetadataAsync(string capture, CancellationToken token)
    {
        var path = Path.Combine(capture, SiteIsolatedGitHubTokens.Metadata);
        SiteIsolatedGitHubFileOperations.RequireRegular(path);
        if (new FileInfo(path).Length > SiteIsolatedGitHubTokens.JsonBytes)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidReceipt);
        }

        var bytes = await File.ReadAllBytesAsync(path, token);
        using var document = JsonDocument.Parse(bytes);
        RequireUniqueProperties(document.RootElement);
        var metadata = JsonNode.Parse(bytes)!.AsObject();
        RequireCallerSources(metadata);
        var repository = SiteIsolatedGitHubInputs.Required(SiteTokens.RepositoryEnvironment);
        var result = await SiteIsolatedGitHubNodeProcess.RunAsync(repository, new
        {
            operation = SiteIsolatedGitHubFields.ReceiptOperation,
            repository,
            receipt = metadata,
        }, token, captureCoverage: false);
        if (!result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean() ||
            metadata[SiteIsolatedGitHubTokens.State]!.GetValue<string>() != SiteIsolatedGitHubTokens.MetadataState)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidReceipt);
        }

        return metadata;
    }

    internal static void RequireCallerSources(JsonObject metadata)
    {
        var source = metadata[SiteIsolatedGitHubTokens.Source];
        if (source?[SiteIsolatedGitHubTokens.Website]?.GetValue<string>() !=
                Environment.GetEnvironmentVariable(SitePublicationTokens.SourceRevisionEnvironment) ||
            source?[SiteIsolatedGitHubTokens.Control]?.GetValue<string>() !=
                Environment.GetEnvironmentVariable(SitePublicationTokens.ControlRevisionEnvironment))
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidReceipt);
        }
    }

    private static void RequireUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidReceipt);
                }

                RequireUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                RequireUniqueProperties(child);
            }
        }
    }
}
