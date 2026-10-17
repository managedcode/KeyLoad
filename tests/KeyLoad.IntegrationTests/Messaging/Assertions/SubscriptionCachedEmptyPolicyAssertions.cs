using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionCachedEmptyPolicyAssertions
{
    internal const string ScopeDenied = "The principal cannot perform this operation in this scope.";
    internal const string EpochChanged = "The principal policy changed since this command was evaluated.";
    private const string McpEpochDenied = "The authenticated principal is not permitted to perform this operation.";

    internal static async Task<string> RefuseAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ReceiveSubscriptionRequest request, string detail, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.SubscriptionStatusAsync(request.Subscription, token));
        var failure = await sdk.ReceiveSubscriptionAsync(request, token);
        await Assert.That(failure.IsFailed).IsTrue();
        await Assert.That(failure.Problem!.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await Assert.That(failure.Problem.Detail).IsEqualTo(detail);
        await Assert.That(failure.Value).IsNull();
        var reply = await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, request, token);
        await DetailAsync(reply, detail);
        await SubscriptionFilterRf3Assertions.EqualAsync(
            await McpCallerAssertions.SdkSuccessAsync(await sdk.SubscriptionStatusAsync(request.Subscription, token)), before);
        return global::System.Text.Json.JsonSerializer.Serialize(failure.Problem, JsonDefaults.Options);
    }

    internal static async Task FencedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionCachedEmptyPolicyState state, CancellationToken token)
    {
        await Assert.That(await RefuseAsync(sdk, mcp, state.Original.Empty, EpochChanged, token))
            .IsEqualTo(state.EmptyPolicyProblem);
        await Assert.That(await RefuseAsync(sdk, mcp, state.Denied, EpochChanged, token))
            .IsEqualTo(state.DeniedPolicyProblem);
        var publish = await sdk.CommitAsync(state.Original.Publish, token);
        await Assert.That(publish.IsFailed).IsTrue();
        await Assert.That(publish.Problem!.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await Assert.That(publish.Problem.Detail).IsEqualTo(EpochChanged);
        await Assert.That(publish.Value).IsNull();
        await DetailAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Original.Publish, token), EpochChanged);
        await SubscriptionFilterRf3Assertions.HistoryAsync(sdk, mcp, state.Original.Empty.Subscription.Source,
            state.Original.Source, token);
    }

    private static async Task DetailAsync(global::ModelContextProtocol.Protocol.CallToolResult reply,
        string expected)
    {
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, true);
        var envelope = reply.StructuredContent ?? throw new InvalidOperationException("The original policy refusal envelope is absent.");
        var detail = expected == ScopeDenied ? ScopeDenied : expected == EpochChanged ? McpEpochDenied
            : throw new InvalidOperationException("The policy refusal oracle is not an admitted literal.");
        await Assert.That(envelope.GetProperty(McpCallerProtocol.Error).GetProperty(McpCallerProtocol.ProblemDetail).GetString())
            .IsEqualTo(detail);
    }
}
