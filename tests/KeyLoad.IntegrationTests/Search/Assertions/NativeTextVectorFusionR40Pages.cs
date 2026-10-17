using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextVectorFusionR40Pages
{
    internal static async Task MatrixAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers reader,
        NativeTextVectorFusionR40State state, long epoch, bool exhaustiveAdministrator, CancellationToken token)
    {
        var restricted = epoch is 3L or 4L;
        foreach (var query in new[] { "needle", "plain", "КИЇВ" })
        {
            foreach (var explain in new[] { false, true })
            {
                if (epoch is 2L or 4L)
                {
                    await NativeTextVectorFusionR40Refusal.BothAsync(admin, reader, state, Request(state, query, true, true, explain), token);
                }
                else
                {
                    await BothAsync(admin, reader, state, query, restricted, false, true, true, explain, token);
                }
            }
        }
        if (epoch is 2L or 4L)
        {
            await NativeTextVectorFusionR40Refusal.BothAsync(admin, reader, state,
            Request(state, "needle", epoch == 4L, epoch == 2L, false), token);
        }
        await BothAsync(admin, reader, state, "needle", restricted, false, epoch != 4L, epoch == 4L, false, token);
        if (exhaustiveAdministrator)
        {
            await AdminAsync(admin, state, token);
        }
        else
        {
            await CurrentAsync(admin, state, token);
        }
    }

    internal static Task CurrentAsync(RequestCqrsRf3Callers admin,
        NativeTextVectorFusionR40State state, CancellationToken token)
        => BothAsync(admin, admin, state, "needle", false, true, true, true, true, token);

    internal static async Task AdminAsync(RequestCqrsRf3Callers admin, NativeTextVectorFusionR40State state, CancellationToken token)
    {
        foreach (var query in new[] { "needle", "plain", "КИЇВ" })
        {
            foreach (var explain in new[] { false, true })
            {
                await BothAsync(admin, admin, state, query, false, true, true, true, explain, token);
            }
        }
    }

    private static async Task BothAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers caller,
        NativeTextVectorFusionR40State state, string query, bool restricted, bool administrator,
        bool text, bool vector, bool explain, CancellationToken token)
    {
        var request = Request(state, query, text, vector, explain);
        var expected = NativeTextVectorFusionR40Literals.Expected(state.Scenario.Partition, query, restricted,
            text, vector, explain);
        foreach (var path in new[] { NativeTextMaintenancePath.Sdk, NativeTextMaintenancePath.Mcp })
        {
            await SqlRf3Protocol.EqualAsync(expected, await NativeTextSelectedRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp, request, path, token));
            if (administrator)
            {
                await OutboxAsync(admin, state, token);
            }
            else
            {
                await CurrentAsync(admin, state, token);
            }
        }
    }

    internal static SearchRequest Request(NativeTextVectorFusionR40State state, string query, bool text,
        bool vector, bool explain)
        => new(state.Scenario.Partition, NativeTextRf3Scenario.Collection,
            text ? NativeTextRf3Scenario.TextField : null, text ? query : null,
            vector ? NativeTextRf3Scenario.VectorField : null, vector ? [1f, 0f] : null,
            vector ? NativeTextRf3Scenario.SpaceFor() : null, 3, Explain: explain,
            TextIndex: text ? new(state.Build.Consumer, state.Build.IndexGeneration) : null);

    internal static async Task OutboxAsync(RequestCqrsRf3Callers admin, NativeTextVectorFusionR40State state, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.Outbox, await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        await SqlRf3Protocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await admin.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
    }
}
