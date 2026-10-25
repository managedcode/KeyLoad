using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateEventsAssertions
{
    internal static async Task ReceiptAsync(FunctionalGateContext context, CommitReceipt receipt, CommandRequest command, StreamRef stream, long revision)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(stream.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(context.ReplicaCount);
        await Assert.That(receipt.Mutations.SequenceEqual(new[]
            { new MutationReceipt("appendEvents", stream.StreamSet, stream.StreamId, revision) })).IsTrue();
    }

    internal static async Task SameAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task BaselineAsync(FunctionalGateContext context, StreamRef stream, CancellationToken token)
    {
        var actual = await context.SdkAsync(context.Reader.Sdk.ReadStreamAsync(new(stream), token));
        await PageAsync(actual, stream, [FunctionalGateEventsProtocol.Event(context, -1, linked: true)], 1);
        var official = await context.McpAsync<StreamPage>(context.Reader, McpCallerTools.StreamsRead, new ReadStreamRequest(stream), token);
        await SameAsync(actual.Events, official.Events);
        await PageAsync(official, stream, [FunctionalGateEventsProtocol.Event(context, -1, linked: true)], 1);
    }

    internal static async Task DeniedAsync(FunctionalGateContext context, StreamRef stream, CancellationToken token)
    {
        var forbidden = new EventData("forbidden", "forbidden", "{\"forbidden\":true}");
        var command = FunctionalGateEventsProtocol.Append(context, stream, forbidden, 0);
        var sdk = await context.Reader.Sdk.CommitAsync(command, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        _ = (await McpCallerAssertions.ErrorAsync(await context.Reader.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, token), ErrorCode.PermissionDenied, dispatched: true)).Value;
    }

    internal static async Task ReadAsync(FunctionalGateContext context, RequestCqrsClusterCallers caller,
        StreamRef stream, EventData[] expected, CommitReceipt receipt, CancellationToken token)
    {
        var sdk = await context.SdkAsync(caller.Sdk.ReadStreamAsync(new(stream), token));
        var mcp = await context.McpAsync<StreamPage>(caller, McpCallerTools.StreamsRead, new ReadStreamRequest(stream), token);
        await PageAsync(sdk, stream, expected, receipt.Token.Position);
        await PageAsync(mcp, stream, expected, receipt.Token.Position);
        await SameAsync(sdk.Events, mcp.Events);
        await TraversalAsync(context, caller, stream, sdk, token);
        await ReplayAsync(context, caller, stream, sdk, token);
        await SourceAsync(context, caller, stream, sdk, token);
    }

    private static async Task PageAsync(StreamPage page, StreamRef stream, EventData[] expected, long minimumCut)
    {
        await Assert.That(page.Stream).IsEqualTo(stream);
        await Assert.That(page.Head).IsEqualTo(new StreamHead(expected.Length, 1, 1));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(minimumCut);
        await Assert.That(page.SnapshotCutPosition).IsEqualTo(page.CutPosition);
        await Assert.That(page.Events.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await EventAsync(expected[index], page.Events[index].Data);
        }
        await Assert.That(page.Events.Select(item => item.Revision).SequenceEqual(
            Enumerable.Range(1, expected.Length).Select(value => (long)value))).IsTrue();
        await Assert.That(page.Events.All(item => item.Stream == stream && item.RecordedAt != default
            && item.EventSequence > 0)).IsTrue();
        await Assert.That(page.Events.Zip(page.Events.Skip(1), (left, right) => left.EventSequence < right.EventSequence).All(value => value)).IsTrue();
    }

    private static async Task EventAsync(EventData expected, EventData actual)
    {
        await Assert.That(actual with
        {
            PayloadJson = expected.PayloadJson,
            HeadersJson = expected.HeadersJson
        }).IsEqualTo(expected);
        using var expectedPayload = JsonDocument.Parse(expected.PayloadJson);
        using var actualPayload = JsonDocument.Parse(actual.PayloadJson);
        await Assert.That(JsonElement.DeepEquals(expectedPayload.RootElement, actualPayload.RootElement)).IsTrue();
        using var expectedHeaders = JsonDocument.Parse(expected.HeadersJson);
        using var actualHeaders = JsonDocument.Parse(actual.HeadersJson);
        await Assert.That(JsonElement.DeepEquals(expectedHeaders.RootElement, actualHeaders.RootElement)).IsTrue();
    }

    private static async Task TraversalAsync(FunctionalGateContext context, RequestCqrsClusterCallers caller,
        StreamRef stream, StreamPage full, CancellationToken token)
    {
        var reverseRequest = new ReadStreamRequest(stream, Direction: StreamReadDirection.Backward);
        var reverse = await context.McpAsync<StreamPage>(caller, McpCallerTools.StreamsRead, reverseRequest, token);
        await SameAsync<EventRecord[]>(full.Events.Reverse().ToArray(), reverse.Events.ToArray());
        await Assert.That(reverse.Head).IsEqualTo(full.Head);
        await Assert.That(reverse.Stream).IsEqualTo(stream);
        await Assert.That(reverse.HasMore).IsFalse();
        await Assert.That(reverse.Cursor).IsNull();
        await Assert.That(reverse.CutPosition).IsGreaterThanOrEqualTo(full.CutPosition);
        await Assert.That(reverse.SnapshotCutPosition).IsEqualTo(reverse.CutPosition);
        if (full.Events.Length == 1)
        {
            return;
        }
        var request = new ReadStreamRequest(stream, Limit: 1);
        var prefix = await context.SdkAsync(caller.Sdk.ReadStreamAsync(request, token));
        await Assert.That(prefix.HasMore).IsTrue();
        await Assert.That(prefix.Cursor).IsNotNull();
        await SameAsync<EventRecord[]>(full.Events.Take(1).ToArray(), prefix.Events.ToArray());
        var suffix = await context.McpAsync<StreamPage>(caller, McpCallerTools.StreamsRead,
            request with
            {
                Cursor = prefix.Cursor
            }, token);
        await SameAsync<EventRecord[]>(full.Events.Skip(1).ToArray(), suffix.Events.ToArray());
        await Assert.That(suffix.HasMore).IsFalse();
        await Assert.That(suffix.Cursor).IsNull();
        await Assert.That(suffix.SnapshotCutPosition).IsEqualTo(prefix.SnapshotCutPosition);
        await Assert.That(suffix.Head).IsEqualTo(full.Head);
    }

    private static async Task ReplayAsync(FunctionalGateContext context, RequestCqrsClusterCallers caller,
        StreamRef stream, StreamPage full, CancellationToken token)
    {
        var request = new ReadAggregateReplayRequest(stream, FunctionalGateEventsProtocol.Reducer,
            FromBeginning: true, MaximumEvents: 2);
        var sdk = await context.SdkAsync(caller.Sdk.ReadAggregateReplayAsync(request, token));
        var mcp = await context.McpAsync<AggregateReplayPage>(caller, McpCallerTools.StreamsReplay, request, token);
        await ReplayPageAsync(sdk, full);
        await ReplayPageAsync(mcp, full);
    }

    private static async Task ReplayPageAsync(AggregateReplayPage actual, StreamPage full)
    {
        await Assert.That(actual.Stream).IsEqualTo(full.Stream);
        await Assert.That(actual.Head).IsEqualTo(full.Head);
        await Assert.That(actual.Snapshot).IsNull();
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(full.CutPosition);
        await SameAsync(full.Events, actual.Events);
    }

    private static async Task SourceAsync(FunctionalGateContext context, RequestCqrsClusterCallers caller,
        StreamRef stream, StreamPage full, CancellationToken token)
    {
        var source = new EventSourceRef(stream.Partition, stream.StreamSet, EventSourceKind.Stream, stream.StreamId);
        var request = new ReadEventSourceRequest(source, Limit: 2);
        var sdk = await context.SdkAsync(caller.Sdk.ReadEventSourceAsync(request, token));
        var mcp = await context.McpAsync<EventSourcePage>(caller, McpCallerTools.EventsRead, request, token);
        await SourcePageAsync(sdk, source, full);
        await SourcePageAsync(mcp, source, full);
    }

    private static async Task SourcePageAsync(EventSourcePage actual, EventSourceRef source, StreamPage full)
    {
        await Assert.That(actual.Source).IsEqualTo(source);
        await Assert.That(actual.Head).IsEqualTo(new EventSourceHead(full.Events.Length, 1, 1));
        await Assert.That(actual.Cursor.Length).IsGreaterThan(0);
        await Assert.That(actual.HasMore).IsFalse();
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(full.CutPosition);
        var expected = full.Events.Select(item => new SourceEventRecord(source, item.Revision,
            item.EventSequence, item.Data, item.RecordedAt)).ToArray();
        await SameAsync<SourceEventRecord[]>(expected, actual.Events.ToArray());
    }
}
