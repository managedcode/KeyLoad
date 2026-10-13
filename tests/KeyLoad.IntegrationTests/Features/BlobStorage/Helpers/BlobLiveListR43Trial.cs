using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobLiveListR43Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, fixture.AdminKey,
            async callers => { await FlowAsync(callers, token); return true; }, token);
    }
    private static async Task FlowAsync(RequestCqrsRf3Callers callers, CancellationToken token)
    {
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(callers.Sdk, token);
        var placementRequest = new AtomicPartitionPlacementReadRequest(1, scenario.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadAtomicPartitionPlacementAsync(placementRequest, token));
        await SqlRf3Protocol.EqualAsync(placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await callers.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, placementRequest, token))).Value);
        var state = new BlobLiveListR43State(scenario.Partition, placement);
        await BlobLiveListR43Publication.PublishAsync(callers, state, 0, token);
        await BlobLiveListR43Publication.PublishAsync(callers, state, 1, token);
        state.Outbox = await BlobLiveListR43Pages.OutboxAsync(callers, state.Partition, token);
        await BlobLiveListR43Pages.AllBytesAsync(callers, state, token);
        var firstRequest = new BlobListRequest(state.Partition, RelationalSqlRf3Tokens.Blobs, 1, null);
        var first = await BlobLiveListR43Pages.BothAsync(callers, state, firstRequest, firstRequest,
            new([state.Versions[0].Completed.Value], BlobLiveListR43State.Id(0)), 0, token);
        await BlobLiveListR43Publication.PublishAsync(callers, state, 2, token);
        state.Phase = 1;
        state.Outbox = await BlobLiveListR43Pages.OutboxAsync(callers, state.Partition, token);
        await BlobLiveListR43Pages.AllBytesAsync(callers, state, token);
        await BlobLiveListR43Publication.PublishAsync(callers, state, 3, token);
        state.Phase = 2;
        state.Outbox = await BlobLiveListR43Pages.OutboxAsync(callers, state.Partition, token);
        await BlobLiveListR43Pages.AllBytesAsync(callers, state, token);
        state.Delete = new(Guid.NewGuid(), state.Versions[1].Begin.Blob, 1L);
        state.Deleted = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.DeleteBlobAsync(state.Delete, token));
        var tombstone = state.Versions[1].Completed.Value with
        { Revision = 2L, VersionId = null, Length = 0L, PartCount = 0, IntegrityHash = null, Deleted = true, UpdatedAt = state.Deleted.Value.UpdatedAt };
        await SqlRf3Protocol.EqualAsync(tombstone, state.Deleted.Value);
        await BlobLiveListR43Publication.ReceiptAsync(state.Placement, state.Delete.CommandId, state.Deleted.Receipt);
        state.Phase = 3;
        state.Outbox = await BlobLiveListR43Pages.OutboxAsync(callers, state.Partition, token);
        await BlobLiveListR43Pages.AllBytesAsync(callers, state, token);
        var sdkResume = new BlobListRequest(state.Partition, RelationalSqlRf3Tokens.Blobs, 1, first[0].NextAfterId);
        var mcpResume = new BlobListRequest(state.Partition, RelationalSqlRf3Tokens.Blobs, 1, first[1].NextAfterId);
        await BlobLiveListR43Pages.BothAsync(callers, state, sdkResume, mcpResume,
            new([state.Versions[3].Completed.Value], BlobLiveListR43State.Id(3)), 3, token);
        var tail = new BlobListRequest(state.Partition, RelationalSqlRf3Tokens.Blobs, 1, BlobLiveListR43State.Id(3));
        await BlobLiveListR43Pages.BothAsync(callers, state, tail, tail, new([], null), 0, token);
        var fresh = new BlobListRequest(state.Partition, RelationalSqlRf3Tokens.Blobs, 8, null);
        await BlobLiveListR43Pages.BothAsync(callers, state, fresh, fresh, new([.. state.Visible()], null), 2, token);
        await BlobLiveListR43Replay.RunAsync(callers, state, token);
    }
}
