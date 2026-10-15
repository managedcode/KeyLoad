using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using P = KeyLoad.IntegrationTests.Features.Search.GraphSearchPathPolicyRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchPathPolicyRf3Calls
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers caller,
        GraphSearchPathPolicyRf3State state, bool visible, CancellationToken token)
    {
        foreach (var (request, expected) in GraphSearchPathPolicyRf3Recipe.Reads(state, visible))
        {
            await ObserveAsync(root, state, async () => await SqlRf3Protocol.EqualAsync(expected,
                await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.GraphSearchAsync(request, token))), token);
            await ObserveAsync(root, state, async () => await SqlRf3Protocol.EqualAsync(expected,
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

    internal static async Task ReplayAsync(RequestCqrsRf3Callers root, GraphSearchPathPolicyRf3State state, CancellationToken token)
    {
        await ObserveAsync(root, state, () => NativeSubmissionColdAssertions.ReplayAsync(root, state.Seed, state.SeedReceipt, token), token);
        if (state.Continuation is { } command && state.ContinuationReceipt is { } receipt)
        {
            await ObserveAsync(root, state, () => NativeSubmissionColdAssertions.ReplayAsync(root, command, receipt, token), token);
        }
    }

    private static async Task HiddenAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers caller,
        GraphSearchPathPolicyRf3State state, string id, CancellationToken token)
    {
        var request = GraphSearchPathPolicyRf3Recipe.HiddenSeed(state.Partition, id);
        await ObserveAsync(root, state, async () =>
        {
            var result = await caller.Sdk.GraphSearchAsync(request, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.NotFound, P.MissingVertex), result.Problem);
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

    private static async Task ObserveAsync(RequestCqrsRf3Callers root, GraphSearchPathPolicyRf3State state,
        Func<Task> original, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(original, failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchPathPolicyRf3Current.RunAsync(root, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
