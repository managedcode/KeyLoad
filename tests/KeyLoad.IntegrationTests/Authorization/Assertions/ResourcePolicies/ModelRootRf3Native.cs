using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class ModelRootRf3Native
{
    private const long EmptyStorageCut = 0;
    internal static async Task RunAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        ModelRootRf3State state, bool read, CancellationToken token)
    {
        if (state.Queue)
        {
            var request = new InspectMessageRequest(new(state.Partition, state.Resource), ModelRootRf3State.Item);
            var expected = new MessageInspectionResult(new(new(ModelRootRf3State.Item, MessageState.Ready, 0, 1L, 1L, null, null),
                Body(state, read, false), Body(state, read, true)));
            await ModelRootRf3Wave.ObserveAsync(root, state, async () => await MessageAsync(
                await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.InspectAsync(request, token)), expected), token);
            await ModelRootRf3Wave.ObserveAsync(root, state, async () => await MessageAsync(
                (await McpCallerAssertions.SuccessAsync<MessageInspectionResult>(await reader.Mcp.CallAsync(
                    McpCallerTools.MessagesInspect, request, token))).Value, expected), token);
        }
        else
        {
            var stream = new StreamRef(state.Partition, state.Resource, ModelRootRf3State.Item);
            var request = new ReadStreamRequest(stream);
            var recordedAt = JsonNode.Parse(state.Original.Json)![ModelRootRf3State.RecordedAtField]!.GetValue<DateTimeOffset>();
            var expected = new EventRecord(stream, 1L, 1L,
                new(ModelRootRf3State.Item, ModelRootRf3State.Type, Body(state, read, false), Body(state, read, true)), recordedAt);
            await ModelRootRf3Wave.ObserveAsync(root, state, async () => await StreamAsync(
                await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadStreamAsync(request, token)), expected), token);
            await ModelRootRf3Wave.ObserveAsync(root, state, async () => await StreamAsync(
                (await McpCallerAssertions.SuccessAsync<StreamPage>(await reader.Mcp.CallAsync(
                    McpCallerTools.StreamsRead, request, token))).Value, expected), token);
        }
    }

    private static string Body(ModelRootRf3State state, bool read, bool header)
    {
        if (state.HeaderRoot == header)
        {
            return read ? ModelRootRf3State.Body : "{}";
        }
        return "{\"public\":\"PUBLIC-VALUE\",\"secret\":\"ROOT-CANARY\"}";
    }
    private static async Task MessageAsync(MessageInspectionResult actual, MessageInspectionResult expected)
    {
        await Assert.That(actual.Message).IsNotNull();
        await SqlRf3Protocol.EqualAsync(expected.Message!.Metadata, actual.Message!.Metadata);
        await JsonAsync(actual.Message.PayloadJson!, expected.Message.PayloadJson!);
        await JsonAsync(actual.Message.HeadersJson!, expected.Message.HeadersJson!);
    }
    private static async Task StreamAsync(StreamPage actual, EventRecord expected)
    {
        await SqlRf3Protocol.EqualAsync(expected.Stream, actual.Stream);
        await SqlRf3Protocol.EqualAsync(new StreamHead(1L, 1L, 1L), actual.Head);
        await Assert.That(actual.HasMore).IsFalse();
        await Assert.That(actual.Cursor).IsNull();
        await Assert.That(actual.CutPosition).IsGreaterThan(EmptyStorageCut);
        await Assert.That(actual.SnapshotCutPosition).IsEqualTo(actual.CutPosition);
        await Assert.That(actual.Events.Length).IsEqualTo(1);
        var record = actual.Events.Single();
        await SqlRf3Protocol.EqualAsync(expected with
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
