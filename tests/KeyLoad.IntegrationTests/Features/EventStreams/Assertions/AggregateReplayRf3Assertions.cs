using System.Globalization;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class AggregateReplayRf3Assertions
{
    internal static async Task SamePageAsync(AggregateReplayPage sdk, AggregateReplayPage mcp,
        int expectedCount, int expectedTailCount, string[] expectedEventIds, string expectedSnapshotState,
        long expectedSnapshotVersion = AggregateReplayRf3Tokens.FirstSnapshotVersion)
    {
        await Assert.That(sdk.Stream).IsEqualTo(mcp.Stream);
        await Assert.That(sdk.Head).IsEqualTo(mcp.Head);
        await Assert.That(sdk.Snapshot).IsEqualTo(mcp.Snapshot);
        await Assert.That(sdk.Snapshot!.StateJson).IsEqualTo(expectedSnapshotState);
        await Assert.That(sdk.Snapshot!.SnapshotVersion).IsEqualTo(expectedSnapshotVersion);
        await Assert.That(sdk.Snapshot.Checksum.Length).IsEqualTo(64);
        await Assert.That(sdk.Events.Length).IsEqualTo(expectedTailCount);
        await Assert.That(sdk.Events.Select(item => item.Data.EventId).SequenceEqual(expectedEventIds)).IsTrue();
        await Assert.That(JsonDefaults.Serialize(sdk.Events).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(mcp.Events))).IsTrue();
        var reducer = new AggregateReplayReducer(AggregateReplayRf3Tokens.Reducer,
            AggregateReplayRf3Tokens.StateSchema, AggregateReplayRf3Tokens.EventSchema,
            "{\"" + AggregateReplayRf3Tokens.CountProperty + "\":0}", Apply);
        var state = AggregateReplayReduction.Reduce(sdk, reducer);
        await Assert.That(state).IsEqualTo("{\"" + AggregateReplayRf3Tokens.CountProperty + "\":"
            + expectedCount.ToString(CultureInfo.InvariantCulture) + "}");
    }

    internal static async Task<CallToolResult> ErrorAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ReadAggregateReplayRequest request, ErrorCode expected, CancellationToken cancellationToken)
    {
        var sdkResult = await sdk.ReadAggregateReplayAsync(request, cancellationToken);
        await Assert.That(sdkResult.Problem?.ErrorCode).IsEqualTo(expected.ToString());
        var reply = await mcp.CallAsync(McpCallerTools.StreamsReplay, request, cancellationToken);
        await McpCallerAssertions.ErrorAsync(reply, expected, dispatched: true);
        return reply;
    }

    private static string Apply(string stateJson, EventRecord record)
    {
        using var state = JsonDocument.Parse(stateJson);
        using var payload = JsonDocument.Parse(record.Data.PayloadJson);
        var count = state.RootElement.GetProperty(AggregateReplayRf3Tokens.CountProperty).GetInt32()
            + payload.RootElement.GetProperty(AggregateReplayRf3Tokens.IncrementProperty).GetInt32();
        return "{\"count\":" + count.ToString(CultureInfo.InvariantCulture) + "}";
    }
}
