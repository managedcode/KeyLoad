using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionIndependentCheckpointR52Refusals
{
    internal static async Task WrongGroupAsync(SubscriptionIndependentCheckpointR52State state,
        string tokenValue, KeyLoadClient sdk, McpOfficialClient mcp, KeyLoadClient admin,
        McpOfficialClient official, CancellationToken token)
    {
        var request = new SubscriptionDeliveryCommand(Guid.NewGuid(), state.Seed.Group, tokenValue, DeliveryAction.Ack);
        var failure = await sdk.CompleteSubscriptionAsync(request, token);
        await FailureAsync(failure, ErrorCode.TokenInvalidated, SubscriptionIndependentCheckpointR52Protocol.ScopeDetail);
        state.Refusals.Add((request, failure, state.PolicyEpoch, ErrorCode.TokenInvalidated));
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        await McpAsync(await mcp.CallAsync(McpCallerTools.SubscriptionsComplete, request, token), ErrorCode.TokenInvalidated);
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
    }

    internal static async Task OldTokenAsync(SubscriptionIndependentCheckpointR52State state, string tokenValue,
        ErrorCode code, string detail, KeyLoadClient sdk, McpOfficialClient mcp,
        KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        var request = new SubscriptionDeliveryCommand(Guid.NewGuid(), state.Seed.Independent, tokenValue, DeliveryAction.Ack);
        var failure = await sdk.CompleteSubscriptionAsync(request, token);
        await FailureAsync(failure, code, detail);
        state.Refusals.Add((request, failure, state.PolicyEpoch, code));
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        await McpAsync(await mcp.CallAsync(McpCallerTools.SubscriptionsComplete, request, token), code);
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
    }

    internal static async Task FailureAsync<T>(ManagedCode.Communication.Result<T> failure, ErrorCode code, string detail)
    {
        await Assert.That(failure.IsFailed).IsTrue();
        await Assert.That(failure.Value).IsNull();
        await SubscriptionFilterClusterAssertions.EqualAsync(failure.Problem, Errors.Problem(code, detail));
    }

    internal static async Task McpAsync(ModelContextProtocol.Protocol.CallToolResult reply, ErrorCode code)
    {
        await McpCallerAssertions.ErrorAsync(reply, code, true);
        var envelope = reply.StructuredContent ?? throw new InvalidOperationException(SubscriptionIndependentCheckpointR52Protocol.MissingState);
        await Assert.That(envelope.GetProperty(McpCallerProtocol.Error).GetProperty(McpCallerProtocol.ProblemDetail).GetString())
            .IsEqualTo(code == ErrorCode.PermissionDenied
                ? SubscriptionIndependentCheckpointR52Protocol.SafePermissionDetail
                : SubscriptionIndependentCheckpointR52Protocol.SafeDetail);
    }
}
