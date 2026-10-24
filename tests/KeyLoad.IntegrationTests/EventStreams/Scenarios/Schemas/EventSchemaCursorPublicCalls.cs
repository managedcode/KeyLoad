using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.EventStreams;

internal static class EventSchemaCursorPublicCalls
{
    internal static async Task<(StreamPage Sdk, StreamPage Mcp)> ReadAsync(RequestCqrsClusterCallers admin,
        RequestCqrsClusterCallers reader, EventSchemaCursorPublicState state, ReadStreamRequest request,
        int[] indices, bool more, long? snapshot, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadStreamAsync(request, token));
        await EventSchemaCursorPublicExpected.PageAsync(state, sdk, indices, true, more, snapshot);
        await EventSchemaCursorPublicCurrent.ObserveAsync(admin, state, token);
        var reply = await reader.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token);
        var mcp = (await McpCallerAssertions.SuccessAsync<StreamPage>(reply)).Value;
        await EventSchemaCursorPublicExpected.PageAsync(state, mcp, indices, true, more, snapshot);
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(reply).Contains(state.Reader.Secret, StringComparison.Ordinal)).IsFalse();
        await EventSchemaCursorPublicCurrent.ObserveAsync(admin, state, token);
        return (sdk, mcp);
    }

    internal static async Task HealthyAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        EventSchemaCursorPublicState state, CancellationToken token)
        => await ReadAsync(admin, reader, state, EventSchemaCursorPublicExpected.Whole(state), [0, 1, 2], false, null, token);

    internal static async Task CaptureAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        EventSchemaCursorPublicState state, CancellationToken token)
    {
        foreach (var direction in new[] { StreamReadDirection.Forward, StreamReadDirection.Backward })
        {
            var indices = direction == StreamReadDirection.Forward ? new[] { 0 } : [2];
            var pages = await ReadAsync(admin, reader, state, EventSchemaCursorPublicExpected.First(state, direction), indices, true, null, token);
            state.Captures.Add((state.Definition.SchemaVersion, direction, pages.Sdk));
            state.Captures.Add((state.Definition.SchemaVersion, direction, pages.Mcp));
        }
        await ContinueAsync(admin, reader, state, token);
    }

    internal static async Task ContinueAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        EventSchemaCursorPublicState state, CancellationToken token)
    {
        foreach (var captured in state.Captures.Where(item => item.Schema == state.Definition.SchemaVersion))
        {
            var indices = captured.Direction == StreamReadDirection.Forward ? new[] { 1, 2 } : [1, 0];
            await ReadAsync(admin, reader, state, EventSchemaCursorPublicExpected.Resume(state, captured.Direction, captured.Page),
                indices, false, captured.Page.SnapshotCutPosition, token);
        }
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        EventSchemaCursorPublicState state, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(state.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(state.Command, token)));
        await HealthyAsync(admin, reader, state, token);
        await SqlClusterProtocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Command, token))).Value);
        await HealthyAsync(admin, reader, state, token);
    }
}
