using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.IntegrationTests.StorageReplication;
using KeyLoad.Server;
using P = KeyLoad.IntegrationTests.Search.GraphSearchPathPolicyClusterProtocol;

namespace KeyLoad.IntegrationTests.Search;

internal static class GraphSearchPathPolicyClusterCalls
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers caller,
        GraphSearchPathPolicyClusterState state, bool visible, CancellationToken token)
    {
        foreach (var (request, expected) in GraphSearchPathPolicyClusterRecipe.Reads(state, visible))
        {
            await ObserveAsync(root, state, async () => await SqlClusterProtocol.EqualAsync(expected,
                await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.GraphSearchAsync(request, token))), token);
            await ObserveAsync(root, state, async () => await SqlClusterProtocol.EqualAsync(expected,
                (await McpCallerAssertions.SuccessAsync<GraphSearchResult>(await caller.Mcp.CallAsync(
                    McpCallerTools.SearchGraph, request, token))).Value), token);
        }
        if (!visible)
        {
            await HiddenAsync(root, caller, state, P.Middle, token);
        }
        if (state.Identity.Principal.RestrictRows)
        {
            await HiddenAsync(root, caller, state, P.Private, token);
        }
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers root, GraphSearchPathPolicyClusterState state, CancellationToken token)
    {
        await ObserveAsync(root, state, () => SubmissionColdAssertions.ReplayAsync(root, state.Seed, state.SeedReceipt, token), token);
        if (state.Continuation is { } command && state.ContinuationReceipt is { } receipt)
        {
            await ObserveAsync(root, state, () => SubmissionColdAssertions.ReplayAsync(root, command, receipt, token), token);
        }
    }

    private static async Task HiddenAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers caller,
        GraphSearchPathPolicyClusterState state, string id, CancellationToken token)
    {
        var request = GraphSearchPathPolicyClusterRecipe.HiddenSeed(state.Partition, id);
        await ObserveAsync(root, state, async () =>
        {
            var result = await caller.Sdk.GraphSearchAsync(request, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.NotFound, P.MissingVertex), result.Problem);
        }, token);
        await ObserveAsync(root, state, async () =>
        {
            var reply = await caller.Mcp.CallAsync(McpCallerTools.SearchGraph, request, token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.NotFound, dispatched: true);
            await Assert.That(JsonElement.DeepEquals(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error),
                JsonSerializer.SerializeToElement(Errors.Problem(ErrorCode.NotFound, P.GenericFailure), JsonDefaults.Options))).IsTrue();
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret, P.PrivateJson);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret, P.MiddleJson);
        }, token);
    }

    private static async Task ObserveAsync(RequestCqrsClusterCallers root, GraphSearchPathPolicyClusterState state,
        Func<Task> original, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(original, failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchPathPolicyClusterCurrent.RunAsync(root, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
