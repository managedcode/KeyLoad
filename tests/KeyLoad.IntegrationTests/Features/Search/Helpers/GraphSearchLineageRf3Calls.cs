using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchLineageRf3Calls
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers caller,
        GraphSearchLineageRf3State state, GraphSearchRequest request, ErrorCode? error, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ObserveAsync(root, state, async () =>
        {
            var result = await caller.Sdk.GraphSearchAsync(request, token);
            if (error is { } code)
            {
                await Assert.That(result.IsFailed).IsTrue();
                await Assert.That(result.Value).IsNull();
                await SqlRf3Protocol.EqualAsync(result.Problem, Errors.Problem(code, Detail(code, false)));
            }
            else
            {
                await SqlRf3Protocol.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(result), Expected(state));
            }
        }, failures, token);
        await ObserveAsync(root, state, () => OfficialAsync(caller, state, McpCallerTools.SearchGraph, request, error, token), failures, token);
        var sql = SqlRf3Protocol.Call(state.Partition, McpCallerTools.SearchGraph, request);
        await ObserveAsync(root, state, async () =>
        {
            if (error is { } code)
            {
                var result = await caller.Sdk.ExecuteSqlAsync(sql, token);
                await Assert.That(result.IsFailed).IsTrue();
                await Assert.That(result.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
                await SqlRf3Protocol.EqualAsync(result.Problem, Errors.Problem(code, Detail(code, false)));
            }
            else
            {
                await SqlRf3Protocol.EqualAsync(await SqlRf3Protocol.SdkAsync<GraphSearchResult>(caller.Sdk, sql, token), Expected(state));
            }
        }, failures, token);
        await ObserveAsync(root, state, () => OfficialAsync(caller, state, SqlOperationProtocol.ToolName, sql, error, token), failures, token);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static GraphSearchResult Expected(GraphSearchLineageRf3State state)
        => GraphSearchLineageRf3Recipe.Expected(state.Partition, state.Expansion, state.EmptyLabels);

    private static string Detail(ErrorCode code, bool official) => code == ErrorCode.PermissionDenied
        ? official ? GraphSearchLineageRf3Protocol.SafeDenied : GraphSearchLineageRf3Protocol.Denied
        : official ? GraphSearchLineageRf3Protocol.GenericFailure : GraphSearchLineageRf3Protocol.MissingVertex;

    private static async Task OfficialAsync(RequestCqrsRf3Callers caller, GraphSearchLineageRf3State state,
        string tool, object request, ErrorCode? error, CancellationToken token)
    {
        var reply = await caller.Mcp.CallAsync(tool, request, token);
        if (error is not { } code)
        {
            await SqlRf3Protocol.EqualAsync((await McpCallerAssertions.SuccessAsync<GraphSearchResult>(reply)).Value, Expected(state));
            return;
        }
        await McpCallerAssertions.ErrorAsync(reply, code, true);
        await Assert.That(JsonElement.DeepEquals(reply.StructuredContent!.Value.GetProperty(GraphSearchLineageRf3Protocol.ErrorProperty),
            JsonSerializer.SerializeToElement(Errors.Problem(code, Detail(code, true)), JsonDefaults.Options))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret, GraphSearchLineageRf3Protocol.Attributes);
    }
    private static async Task ObserveAsync(RequestCqrsRf3Callers root, GraphSearchLineageRf3State state,
        Func<Task> operation, List<Exception> failures, CancellationToken token)
    {
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageRf3Current.RunAsync(root, state, token), failures);
    }
    internal static async Task ReplayAsync(RequestCqrsRf3Callers root, GraphSearchLineageRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ObserveAsync(root, state, async () =>
        {
            await SqlRf3Protocol.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(state.Seed, token)), state.Receipt);
        }, failures, token);
        await ObserveAsync(root, state, async () =>
        {
            await SqlRf3Protocol.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await root.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, state.Seed, token))).Value, state.Receipt);
        }, failures, token);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
