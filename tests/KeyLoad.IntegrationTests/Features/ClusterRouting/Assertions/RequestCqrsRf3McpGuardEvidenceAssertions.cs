using System.Text;
using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3McpGuardEvidenceAssertions
{
    private const int ArtifactVersion = 1;
    private const int MaximumArtifactBytes = 16 * 1_024;
    private const string ExpectedNode = RequestCqrsRf3Protocol.Node1;

    internal static async Task VerifyAsync(string path, string persistedKey, CancellationToken cancellationToken)
    {
        var bytes = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        await VerifyClosedFieldsAsync(document.RootElement).ConfigureAwait(false);
        var artifact = document.RootElement.Deserialize<RequestCqrsRf3McpRejectionArtifact>()
            ?? throw new InvalidDataException("The MCP guard artifact is empty.");
        await Assert.That(artifact.Version).IsEqualTo(ArtifactVersion);
        await Assert.That(artifact.WaveId).IsNotEqualTo(Guid.Empty);
        if (artifact.Rejections is not { Length: 1 } records)
        { throw new InvalidDataException("The MCP guard artifact does not contain exactly one closed rejection."); }
        await VerifyRecordAsync(records[0], artifact.WaveId).ConfigureAwait(false);
        var text = Encoding.UTF8.GetString(bytes);
        await Assert.That(text.Contains(persistedKey, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(RequestCqrsRf3McpGuardEvidenceCall.MalformedPayload,
            StringComparison.Ordinal)).IsFalse();
    }

    private static async Task VerifyClosedFieldsAsync(JsonElement artifact)
    {
        await VerifyFieldsAsync(artifact, nameof(RequestCqrsRf3McpRejectionArtifact.Version),
            nameof(RequestCqrsRf3McpRejectionArtifact.WaveId), nameof(RequestCqrsRf3McpRejectionArtifact.Rejections))
            .ConfigureAwait(false);
        var rows = artifact.GetProperty(nameof(RequestCqrsRf3McpRejectionArtifact.Rejections));
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != 1)
        { throw new InvalidDataException("The MCP guard artifact rejection array is invalid."); }
        await VerifyFieldsAsync(rows[0], nameof(RequestCqrsRf3McpRejectionRecord.WaveId),
            nameof(RequestCqrsRf3McpRejectionRecord.Node), nameof(RequestCqrsRf3McpRejectionRecord.Stage),
            nameof(RequestCqrsRf3McpRejectionRecord.MethodCategory)).ConfigureAwait(false);
    }

    private static async Task VerifyFieldsAsync(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object)
        { throw new InvalidDataException("The MCP guard artifact contains a non-object record."); }
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        await Assert.That(names.Length).IsEqualTo(expected.Length);
        await Assert.That(names.ToHashSet(StringComparer.Ordinal).SetEquals(expected)).IsTrue();
    }

    private static async Task VerifyRecordAsync(RequestCqrsRf3McpRejectionRecord record, Guid waveId)
    {
        await Assert.That(record.WaveId).IsEqualTo(waveId);
        await Assert.That(record.Node).IsEqualTo(ExpectedNode);
        await Assert.That(record.Stage).IsEqualTo(McpTransportStage.BodyMethodMismatch);
        await Assert.That(record.MethodCategory).IsEqualTo(McpTransportMethodCategory.ToolsCall);
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            MaximumArtifactBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var expectedLength = stream.Length;
        if (expectedLength is < 1 or > MaximumArtifactBytes)
        { throw new InvalidDataException("The MCP guard artifact exceeds its bounded size."); }
        var buffer = new byte[MaximumArtifactBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            { break; }
            total += count;
        }
        if (total != expectedLength || total > MaximumArtifactBytes || stream.Length != expectedLength)
        { throw new InvalidDataException("The MCP guard artifact changed while being read."); }
        return buffer[..total];
    }
}
