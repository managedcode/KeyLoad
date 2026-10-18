using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchLineageClusterCalls
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers caller,
        GraphSearchLineageClusterState state, GraphSearchRequest request, ErrorCode? error, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ObserveAsync(root, state, async () =>
        {
            var result = await caller.Sdk.GraphSearchAsync(request, token);
            if (error is { } code)
            {
                await Assert.That(result.IsFailed).IsTrue();
                await Assert.That(result.Value).IsNull();
                await SqlClusterProtocol.EqualAsync(result.Problem, Errors.Problem(code, Detail(code, false)));
            }
            else
            {
                await SqlClusterProtocol.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(result), Expected(state));
            }
        }, failures, token);
        await ObserveAsync(root, state, () => OfficialAsync(caller, state, McpCallerTools.SearchGraph, request, error, token), failures, token);
        var sql = SqlClusterProtocol.Call(state.Partition, McpCallerTools.SearchGraph, request);
        await ObserveAsync(root, state, async () =>
        {
            if (error is { } code)
            {
                var result = await caller.Sdk.ExecuteSqlAsync(sql, token);
                await Assert.That(result.IsFailed).IsTrue();
                await Assert.That(result.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
                await SqlClusterProtocol.EqualAsync(result.Problem, Errors.Problem(code, Detail(code, false)));
            }
            else
            {
                await SqlClusterProtocol.EqualAsync(await SqlClusterProtocol.SdkAsync<GraphSearchResult>(caller.Sdk, sql, token), Expected(state));
            }
        }, failures, token);
        await ObserveAsync(root, state, () => OfficialAsync(caller, state, SqlOperationProtocol.ToolName, sql, error, token), failures, token);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static GraphSearchResult Expected(GraphSearchLineageClusterState state)
        => GraphSearchLineageClusterRecipe.Expected(state.Partition, state.Expansion, state.EmptyLabels);

    private static string Detail(ErrorCode code, bool official) => code == ErrorCode.PermissionDenied
        ? official ? GraphSearchLineageClusterProtocol.SafeDenied : GraphSearchLineageClusterProtocol.Denied
        : official ? GraphSearchLineageClusterProtocol.GenericFailure : GraphSearchLineageClusterProtocol.MissingVertex;

    private static async Task OfficialAsync(RequestCqrsClusterCallers caller, GraphSearchLineageClusterState state,
        string tool, object request, ErrorCode? error, CancellationToken token)
    {
        var reply = await caller.Mcp.CallAsync(tool, request, token);
        if (error is not { } code)
        {
            await SqlClusterProtocol.EqualAsync((await McpCallerAssertions.SuccessAsync<GraphSearchResult>(reply)).Value, Expected(state));
            return;
        }
        await McpCallerAssertions.ErrorAsync(reply, code, true);
        await Assert.That(JsonElement.DeepEquals(reply.StructuredContent!.Value.GetProperty(GraphSearchLineageClusterProtocol.ErrorProperty),
            JsonSerializer.SerializeToElement(Errors.Problem(code, Detail(code, true)), JsonDefaults.Options))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret, GraphSearchLineageClusterProtocol.Attributes);
    }
    private static async Task ObserveAsync(RequestCqrsClusterCallers root, GraphSearchLineageClusterState state,
        Func<Task> operation, List<Exception> failures, CancellationToken token)
    {
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageClusterCurrent.RunAsync(root, state, token), failures);
    }
    internal static async Task ReplayAsync(RequestCqrsClusterCallers root, GraphSearchLineageClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ObserveAsync(root, state, async () =>
        {
            await SqlClusterProtocol.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(state.Seed, token)), state.Receipt);
        }, failures, token);
        await ObserveAsync(root, state, async () =>
        {
            await SqlClusterProtocol.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await root.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, state.Seed, token))).Value, state.Receipt);
        }, failures, token);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
