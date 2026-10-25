using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed class SiteIsolatedGitHubArchiveReceipt(string capture, string receiptPath,
    JsonObject value, SiteIsolatedGitHubArchiveFile[] files)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private JsonObject OriginalValue { get; } = value.DeepClone().AsObject();
    private SiteIsolatedGitHubArchiveFile[] OriginalFiles { get; } = [.. files];

    public string Capture { get; } = capture;

    public string ReceiptPath { get; } = receiptPath;

    public JsonObject Value => OriginalValue.DeepClone().AsObject();

    public SiteIsolatedGitHubArchiveFile[] Files => [.. OriginalFiles];

    internal bool Matches(JsonNode? candidate) => JsonNode.DeepEquals(OriginalValue, candidate);

    internal bool MatchesBytes(ReadOnlySpan<byte> candidate) =>
        candidate.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(OriginalValue, Options));
}
