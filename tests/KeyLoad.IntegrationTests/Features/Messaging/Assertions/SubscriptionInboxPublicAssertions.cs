using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionInboxPublicAssertions
{
    private const string First = "inbox-first";
    private const string Second = "inbox-second";
    internal static global::System.Collections.Immutable.ImmutableArray<Mutation> Effects
    {
        get;
    } =
        [new PutDocument(SubscriptionFilterRf3Protocol.Collection, First, "{\"n\":1}", 0),
         new PutDocument(SubscriptionFilterRf3Protocol.Collection, Second, "{\"n\":2}", 0)];
    internal static global::System.Collections.Immutable.ImmutableArray<Mutation> ChangedEffects
    {
        get;
    } =
        [new PutDocument(SubscriptionFilterRf3Protocol.Collection, First, "{\"n\":99}", 1),
         new PutDocument(SubscriptionFilterRf3Protocol.Collection, Second, "{\"n\":2}", 0)];

    internal static async Task DocumentsAsync(KeyLoadClient sdk, SubscriptionRef group, CancellationToken token)
    {
        foreach (var expected in new[] { (First, "{\"n\":1}"), (Second, "{\"n\":2}") })
        {
            var actual = await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(
                new(group.Source.Partition, SubscriptionFilterRf3Protocol.Collection, expected.Item1), token));
            await Assert.That(actual!.Json).IsEqualTo(expected.Item2);
            await Assert.That(actual.Revision).IsEqualTo(1L);
        }
    }

    internal static async Task<string> ConflictAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionProcessingRequest request, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.SubscriptionStatusAsync(request.Subscription, token));
        var failure = await sdk.CommitSubscriptionProcessingAsync(request, token);
        await Assert.That(failure.IsFailed).IsTrue();
        await Assert.That(failure.Problem!.ErrorCode).IsEqualTo(ErrorCode.Conflict.ToString());
        await Assert.That(failure.Value).IsNull();
        var reply = await mcp.CallAsync(McpCallerTools.SubscriptionsProcess, request, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.Conflict, true);
        var envelope = reply.StructuredContent ?? throw new InvalidOperationException("The canonical conflict envelope is absent.");
        var problem = envelope.GetProperty(McpCallerProtocol.Error);
        await Assert.That(problem.GetProperty(McpCallerProtocol.ProblemType).GetString()).IsEqualTo("urn:keyload:error:Conflict");
        await Assert.That(problem.GetProperty(McpCallerProtocol.ProblemTitle).GetString()).IsEqualTo("Conflict");
        await Assert.That(problem.GetProperty(McpCallerProtocol.ProblemStatus).GetInt32()).IsEqualTo(409);
        await Assert.That(problem.GetProperty(McpCallerProtocol.ProblemDetail).GetString())
            .IsEqualTo("The database operation could not be completed.");
        await SubscriptionFilterRf3Assertions.EqualAsync(
            await McpCallerAssertions.SdkSuccessAsync(await sdk.SubscriptionStatusAsync(request.Subscription, token)), before);
        await DocumentsAsync(sdk, request.Subscription, token);
        return global::System.Text.Json.JsonSerializer.Serialize(failure.Problem, JsonDefaults.Options);
    }

    internal static async Task OriginalFencedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionInboxPublicState state, CancellationToken token)
    {
        var result = await sdk.CommitSubscriptionProcessingAsync(state.Processing, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(ErrorCode.TokenInvalidated.ToString());
        await Assert.That(result.Value).IsNull();
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SubscriptionsProcess,
            state.Processing, token), ErrorCode.TokenInvalidated, true);
        await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, state.First, ErrorCode.TokenInvalidated, token);
        await DocumentsAsync(sdk, state.First.Subscription, token);
    }
}
