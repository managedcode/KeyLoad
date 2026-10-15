using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class HeavyRouteRf3Operations
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader, HeavyRouteRf3State state,
        ErrorCode? error, CancellationToken token)
    {
        await HeavyRouteRf3Current.ObserveAsync(root, state, async () => await SdkAsync(
            await reader.Sdk.ShortestPathAsync(state.Path, token), error, value => HeavyRouteRf3Results.PathAsync(value, state)), token);
        await HeavyRouteRf3Current.ObserveAsync(root, state, async () => await SdkAsync(
            await reader.Sdk.IncomingEdgesAsync(state.Incoming, token), error, value => HeavyRouteRf3Results.IncomingAsync(value, state)), token);
        await HeavyRouteRf3Current.ObserveAsync(root, state, async () => await SdkAsync(
            await reader.Sdk.GraphSearchAsync(state.Search, token), error, value => HeavyRouteRf3Results.SearchAsync(value, state)), token);
        await HeavyRouteRf3Current.ObserveAsync(root, state, async () => await SdkAsync(
            await reader.Sdk.ApproximateSearchAsync(state.Approximate, token), error, value => HeavyRouteRf3Results.AnnAsync(value, state)), token);
        await OfficialAsync(root, reader, state, McpCallerTools.GraphShortestPath, state.Path,
            error, (GraphShortestPathResult value) => HeavyRouteRf3Results.PathAsync(value, state), token);
        await OfficialAsync(root, reader, state, McpCallerTools.GraphIncomingEdges, state.Incoming,
            error, (GraphIncomingEdgesPageV1 value) => HeavyRouteRf3Results.IncomingAsync(value, state), token);
        await OfficialAsync(root, reader, state, McpCallerTools.SearchGraph, state.Search,
            error, (GraphSearchResult value) => HeavyRouteRf3Results.SearchAsync(value, state), token);
        await OfficialAsync(root, reader, state, AnnSearchProtocol.Tool, state.Approximate,
            error, (AnnSearchPage value) => HeavyRouteRf3Results.AnnAsync(value, state), token);
    }
    private static async Task SdkAsync<T>(Result<T> actual, ErrorCode? error, Func<T, Task> healthy)
    {
        if (error is { } code)
        {
            await Assert.That(actual.IsSuccess).IsFalse();
            await Assert.That(actual.Value).IsNull();
            var detail = code == ErrorCode.ResourceExhausted
                ? "The node, tenant or principal command admission budget is exhausted."
                : "The principal cannot perform this operation in this scope.";
            await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), actual.Problem);
            return;
        }
        await healthy(await McpCallerAssertions.SdkSuccessAsync(actual));
    }
    private static Task OfficialAsync<T>(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader, HeavyRouteRf3State state,
        string tool, object request, ErrorCode? error, Func<T, Task> healthy, CancellationToken token)
        => HeavyRouteRf3Current.ObserveAsync(root, state, async () =>
        {
            var reply = await reader.Mcp.CallAsync(tool, request, token);
            if (error is { } code)
            {
                await McpCallerAssertions.ErrorAsync(reply, code, dispatched: code != ErrorCode.ResourceExhausted);
            }
            else
            {
                await healthy((await McpCallerAssertions.SuccessAsync<T>(reply)).Value);
            }
        }, token);
}
