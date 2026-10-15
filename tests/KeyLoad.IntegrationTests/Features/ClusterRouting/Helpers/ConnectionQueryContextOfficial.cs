using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ConnectionQueryContextOfficial
{
    internal static async Task RunAsync(ConnectionRf3Scenario scenario, ConnectionQueryContextState state, CancellationToken token)
    {
        var caller = await scenario.CallerAsync(token);
        var independent = caller.WithCredential(state.Second.Identity.Secret);
        var second = await ConnectionQueryContextPages.SdkAsync(scenario, state, independent, state.Second, null, token);
        var official = await ConnectionQueryContextPages.McpAsync(scenario, state, caller, token);
        await ConnectionRf3WitnessReader.SameOwnerAsync(second.Witness, official.Witness);
        await ConnectionQueryContextProvisioning.PolicyAsync(scenario, state.First.Identity, false, 5L, token);
        for (var route = 0; route < 3; route++)
        {
            await ConnectionQueryContextCurrent.ObserveAsync(scenario, state, async () =>
            {
                var denied = await scenario.ObserveAsync(state.First.Identity, Guid.Empty,
                    route == 0 ? GrainReadKind.Query : GrainReadKind.AstQuery,
                    () => route == 0 ? caller.CallAsync(McpCallerTools.QueryExecute,
                        ConnectionQueryContextRequests.Sql(state.First.Identity.Partition), token)
                        : caller.CallAsync(McpCallerTools.QueryAst, route == 1
                            ? ConnectionQueryContextRequests.Ast(state.First.Identity.Partition)
                            : ConnectionQueryContextRequests.CSharp(state.First.Identity.Partition), token), token);
                await ProblemAsync(denied.Result, ErrorCode.PermissionDenied, "The principal cannot perform this operation in this scope.");
                await ConnectionRf3WitnessReader.SameOwnerAsync(official.Witness, denied.Witness);
            }, token);
        }
        await ConnectionQueryContextPages.SdkAsync(scenario, state, independent, state.Second, official.Witness, token);
        await ConnectionQueryContextProvisioning.PolicyAsync(scenario, state.First.Identity, true, 6L, token);
        await ConnectionQueryContextCurrent.ObserveAsync(scenario, state, async () =>
        {
            var stale = await scenario.ObserveAsync(state.First.Identity, Guid.Empty, GrainReadKind.AstQuery,
                () => caller.CallAsync(McpCallerTools.QueryAst,
                    ConnectionQueryContextRequests.Ast(state.First.Identity.Partition, official.Page.Cursor), token), token);
            await ProblemAsync(stale.Result, ErrorCode.CursorExpired, "The database operation could not be completed.");
        }, token);
        var repaired = await ConnectionQueryContextPages.McpAsync(scenario, state, caller, token);
        await ConnectionRf3WitnessReader.SameOwnerAsync(official.Witness, repaired.Witness);
        var sdk = await ConnectionQueryContextPages.SdkAsync(scenario, state, caller.Sdk, state.First, repaired.Witness, token);
        await scenario.CloseAsync(caller, sdk.Marker, token);
    }
    private static async Task ProblemAsync(CallToolResult reply, ErrorCode code, string detail)
    {
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true);
        var expected = JsonDefaults.Deserialize<System.Text.Json.JsonElement>(JsonDefaults.Serialize(Errors.Problem(code, detail)));
        await SqlRf3Protocol.EqualAsync(expected, reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error));
    }
}
