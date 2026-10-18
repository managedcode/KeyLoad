using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventCurrentReadAuthorityClusterCurrent
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers administrator,
        EventCurrentReadAuthorityClusterState state, CancellationToken token)
    {
        var request = new ReadStreamRequest(state.Scenario.Stream);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadStreamAsync(request, token));
        await CompleteAsync(state, sdk);
        var mcp = (await McpCallerAssertions.SuccessAsync<StreamPage>(await administrator.Mcp.CallAsync(
            McpCallerTools.StreamsRead, request, token))).Value;
        await CompleteAsync(state, mcp);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
        await SqlClusterProtocol.EqualAsync(state.CurrentOutbox, outbox);
        await SqlClusterProtocol.EqualAsync(state.CurrentOutbox,
            (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await administrator.Mcp.CallAsync(
                McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
    }

    internal static async Task BaselineAsync(RequestCqrsClusterCallers administrator,
        EventCurrentReadAuthorityClusterState state, CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadStreamAsync(
            new(state.Scenario.Stream), token));
        await EventCurrentReadAuthorityClusterExpected.PageAsync(state, page,
            EventCurrentReadAuthorityClusterExpected.Data(state.Updated), 1L,
            state.Updated ? 4L : 3L,
            null, false);
        if (state.Updated)
        {
            await SqlClusterProtocol.EqualAsync(state.CurrentPage.Events.Take(3).ToArray(), page.Events.Take(3).ToArray());
        }
        else
        {
            await SqlClusterProtocol.EqualAsync(state.CurrentPage.Events.ToArray(), page.Events.ToArray());
            await SqlClusterProtocol.EqualAsync(state.CurrentPage.Head, page.Head);
        }
        state.CurrentPage = page;
        state.CurrentOutbox = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
        await ObserveAsync(administrator, state, token);
    }

    private static async Task CompleteAsync(EventCurrentReadAuthorityClusterState state, StreamPage page)
    {
        await EventCurrentReadAuthorityClusterExpected.PageAsync(state, page,
            EventCurrentReadAuthorityClusterExpected.Data(state.Updated), 1L, state.Updated ? 4L : 3L,
            null, false);
        await SqlClusterProtocol.EqualAsync(state.CurrentPage.Head, page.Head);
        await SqlClusterProtocol.EqualAsync(state.CurrentPage.Events.ToArray(), page.Events.ToArray());
    }
}
