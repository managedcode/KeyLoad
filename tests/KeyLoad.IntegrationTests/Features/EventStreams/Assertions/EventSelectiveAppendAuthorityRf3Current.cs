using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventSelectiveAppendAuthorityRf3Current
{
    internal static async Task ObserveAsync(RequestCqrsRf3Callers administrator,
        EventSelectiveAppendAuthorityRf3State state, CancellationToken token)
    {
        var request = new ReadStreamRequest(state.Scenario.Stream);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadStreamAsync(request, token));
        await CompleteAsync(state, sdk);
        var mcp = (await McpCallerAssertions.SuccessAsync<StreamPage>(await administrator.Mcp.CallAsync(
            McpCallerTools.StreamsRead, request, token))).Value;
        await CompleteAsync(state, mcp);
        await SqlRf3Protocol.EqualAsync(state.Placement, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ReadAtomicPartitionPlacementAsync(
                new AtomicPartitionPlacementReadRequest(1, state.Scenario.Partition), token)));
        var outbox = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
        await SqlRf3Protocol.EqualAsync(state.CurrentOutbox, outbox);
        await SqlRf3Protocol.EqualAsync(state.CurrentOutbox,
            (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await administrator.Mcp.CallAsync(
                McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
    }

    internal static async Task BaselineAsync(RequestCqrsRf3Callers administrator,
        EventSelectiveAppendAuthorityRf3State state, CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadStreamAsync(
            new(state.Scenario.Stream), token));
        await EventSelectiveAppendAuthorityRf3Expected.PageAsync(state, page,
            EventSelectiveAppendAuthorityRf3Expected.Data(state.Updated), 1L,
            state.Updated ? 4L : 3L,
            null, false);
        if (state.Updated)
        {
            await SqlRf3Protocol.EqualAsync(state.CurrentPage.Events.Take(3).ToArray(), page.Events.Take(3).ToArray());
        }
        else
        {
            await SqlRf3Protocol.EqualAsync(state.CurrentPage.Events.ToArray(), page.Events.ToArray());
            await SqlRf3Protocol.EqualAsync(state.CurrentPage.Head, page.Head);
        }
        state.CurrentPage = page;
        state.CurrentOutbox = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
        await ObserveAsync(administrator, state, token);
    }

    private static async Task CompleteAsync(EventSelectiveAppendAuthorityRf3State state, StreamPage page)
    {
        await EventSelectiveAppendAuthorityRf3Expected.PageAsync(state, page,
            EventSelectiveAppendAuthorityRf3Expected.Data(state.Updated), 1L, state.Updated ? 4L : 3L,
            null, false);
        await SqlRf3Protocol.EqualAsync(state.CurrentPage.Head, page.Head);
        await SqlRf3Protocol.EqualAsync(state.CurrentPage.Events.ToArray(), page.Events.ToArray());
    }
}
