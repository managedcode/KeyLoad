using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal static class ModelRootCluster
{
    private const long EmptyStorageCut = 0;
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        ModelRootClusterState state, bool read, CancellationToken token)
    {
        if (state.Queue)
        {
            var request = new InspectMessageRequest(new(state.Partition, state.Resource), ModelRootClusterState.Item);
            var expected = new MessageInspectionResult(new(new(ModelRootClusterState.Item, MessageState.Ready, 0, 1L, 1L, null, null),
                Body(state, read, false), Body(state, read, true)));
            await ModelRootClusterWave.ObserveAsync(root, state, async () => await MessageAsync(
                await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.InspectAsync(request, token)), expected), token);
            await ModelRootClusterWave.ObserveAsync(root, state, async () => await MessageAsync(
                (await McpCallerAssertions.SuccessAsync<MessageInspectionResult>(await reader.Mcp.CallAsync(
                    McpCallerTools.MessagesInspect, request, token))).Value, expected), token);
        }
        else
        {
            var stream = new StreamRef(state.Partition, state.Resource, ModelRootClusterState.Item);
            var request = new ReadStreamRequest(stream);
            var original = JsonNode.Parse(state.Original.Json)!;
            var recordedAt = original[ModelRootClusterState.RecordedAtField]!.GetValue<DateTimeOffset>();
            // The event sequence counts every event in the partition, so it is the one the original row reported.
            var sequence = original[ModelRootClusterState.EventSequenceField]!.GetValue<long>();
            var expected = new EventRecord(stream, 1L, sequence,
                new(ModelRootClusterState.Item, ModelRootClusterState.Type, Body(state, read, false), Body(state, read, true)), recordedAt);
            await ModelRootClusterWave.ObserveAsync(root, state, async () => await StreamAsync(
                await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadStreamAsync(request, token)), expected), token);
            await ModelRootClusterWave.ObserveAsync(root, state, async () => await StreamAsync(
                (await McpCallerAssertions.SuccessAsync<StreamPage>(await reader.Mcp.CallAsync(
                    McpCallerTools.StreamsRead, request, token))).Value, expected), token);
        }
    }

    private static string Body(ModelRootClusterState state, bool read, bool header)
    {
        if (state.HeaderRoot == header)
        {
            return read ? ModelRootClusterState.Body : "{}";
        }
        return "{\"public\":\"PUBLIC-VALUE\",\"secret\":\"ROOT-CANARY\"}";
    }
    private static async Task MessageAsync(MessageInspectionResult actual, MessageInspectionResult expected)
    {
        await Assert.That(actual.Message).IsNotNull();
        await SqlClusterProtocol.EqualAsync(expected.Message!.Metadata, actual.Message!.Metadata);
        await JsonAsync(actual.Message.PayloadJson!, expected.Message.PayloadJson!);
        await JsonAsync(actual.Message.HeadersJson!, expected.Message.HeadersJson!);
    }
    private static async Task StreamAsync(StreamPage actual, EventRecord expected)
    {
        await SqlClusterProtocol.EqualAsync(expected.Stream, actual.Stream);
        await SqlClusterProtocol.EqualAsync(new StreamHead(1L, 1L, 1L), actual.Head);
        await Assert.That(actual.HasMore).IsFalse();
        await Assert.That(actual.Cursor).IsNull();
        await Assert.That(actual.CutPosition).IsGreaterThan(EmptyStorageCut);
        await Assert.That(actual.SnapshotCutPosition).IsEqualTo(actual.CutPosition);
        await Assert.That(actual.Events.Length).IsEqualTo(1);
        var record = actual.Events.Single();
        await SqlClusterProtocol.EqualAsync(expected with
        {
            Data = expected.Data with
            {
                PayloadJson = "",
                HeadersJson = ""
            }
        },
            record with
            {
                Data = record.Data with
                {
                    PayloadJson = "",
                    HeadersJson = ""
                }
            });
        await JsonAsync(record.Data.PayloadJson, expected.Data.PayloadJson);
        await JsonAsync(record.Data.HeadersJson, expected.Data.HeadersJson);
    }
    private static async Task JsonAsync(string actual, string expected)
    {
        using var left = JsonDocument.Parse(actual);
        using var right = JsonDocument.Parse(expected);
        await Assert.That(JsonElement.DeepEquals(left.RootElement, right.RootElement)).IsTrue();
    }
}
