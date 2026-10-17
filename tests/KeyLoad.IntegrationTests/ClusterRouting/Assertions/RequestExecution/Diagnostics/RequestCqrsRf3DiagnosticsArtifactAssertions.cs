using System.Text;
using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

internal static class RequestCqrsRf3DiagnosticsArtifactAssertions
{
    private const string SensitiveSentinel = "private-diagnostic-sentinel-7fe26d";
    private const string RejectionsProperty = "Rejections";
    private const string VersionProperty = "Version";
    private const string WaveIdProperty = "WaveId";
    private const string MethodCategoryProperty = "MethodCategory";
    private const string NodeProperty = "Node";
    private const string StageProperty = "Stage";
    private const int MaximumArtifactBytes = RequestCqrsRf3DiagnosticsArtifactFiles.MaximumArtifactBytes;

    internal static async Task AssertAsync(byte[] bytes, Guid waveId, int perNode)
    {
        await Assert.That(bytes.Length).IsLessThanOrEqualTo(MaximumArtifactBytes);
        var text = Encoding.UTF8.GetString(bytes);
        await Assert.That(text.Contains(SensitiveSentinel, StringComparison.Ordinal)).IsFalse();
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        await AssertPropertiesAsync(root, RejectionsProperty, VersionProperty, WaveIdProperty).ConfigureAwait(false);
        await Assert.That(root.GetProperty(VersionProperty).GetInt32()).IsEqualTo(1);
        await Assert.That(root.GetProperty(WaveIdProperty).GetGuid()).IsEqualTo(waveId);
        await Assert.That(waveId != Guid.Empty).IsTrue();
        await AssertRecordsAsync(root.GetProperty(RejectionsProperty), waveId, perNode).ConfigureAwait(false);
    }

    private static async Task AssertRecordsAsync(JsonElement values, Guid waveId, int perNode)
    {
        var records = values.EnumerateArray().ToArray();
        await Assert.That(records.Length).IsEqualTo(RequestCqrsRf3Protocol.NodeCount * perNode);
        foreach (var record in records)
        {
            await AssertPropertiesAsync(record, MethodCategoryProperty, NodeProperty, StageProperty, WaveIdProperty).ConfigureAwait(false);
            await Assert.That(record.GetProperty(WaveIdProperty).GetGuid()).IsEqualTo(waveId);
            await Assert.That(record.GetProperty(StageProperty).GetInt32()).IsEqualTo((int)McpTransportStage.ProtocolRevisionValue);
            await Assert.That(record.GetProperty(MethodCategoryProperty).GetInt32())
                .IsEqualTo((int)McpTransportMethodCategory.ToolsCall);
        }
        foreach (var node in new[] { RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2,
                     RequestCqrsRf3Protocol.Node3 })
        {
            await Assert.That(records.Count(record => string.Equals(record.GetProperty(NodeProperty).GetString(), node, StringComparison.Ordinal)))
                .IsEqualTo(perNode);
        }
    }

    private static async Task AssertPropertiesAsync(JsonElement value, params string[] expected)
    {
        var actual = value.EnumerateObject().Select(property => property.Name)
            .Order(StringComparer.Ordinal).ToArray();
        await Assert.That(actual.SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal)).IsTrue();
    }
}
