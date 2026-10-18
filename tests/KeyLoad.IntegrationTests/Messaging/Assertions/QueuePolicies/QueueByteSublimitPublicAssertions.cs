using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueByteSublimitPublicAssertions
{
    internal const string First = "byte-pending-first";
    internal const string Second = "byte-ready-second";
    internal const string Healthy = "byte-healthy-third";
    internal const string Witness = "byte-admission-witness";

    internal static async Task ImageAsync(RequestCqrsClusterCallers callers, QueueByteSublimitPublicState state,
        MessageMetadata first, MessageMetadata second, CancellationToken token)
    {
        await QueueLeasedCancelPublicAssertions.MessageAsync(callers, state.Owner, First,
            new(first, first.State == MessageState.Acked ? null : QueueLifecyclePublicProtocol.CanonicalPayload,
                first.State == MessageState.Acked ? null : QueueLifecyclePublicProtocol.Headers), token);
        await QueueLeasedCancelPublicAssertions.MessageAsync(callers, state.Owner, Second,
            new(second, second.State == MessageState.Acked ? null : QueueLifecyclePublicProtocol.CanonicalPayload,
                second.State == MessageState.Acked ? null : QueueLifecyclePublicProtocol.Headers), token);
        var witness = await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(
            new(state.Owner.Partition, QueueLifecyclePublicProtocol.Collection, Witness), token));
        await Assert.That(witness!.Json).IsEqualTo(QueueLifecyclePublicProtocol.Payload);
        await Assert.That(witness.Revision).IsEqualTo(1L);
    }

    internal static async Task RefuseAsync(RequestCqrsClusterCallers callers, QueueByteSublimitPublicState state,
        CommandRequest request, CancellationToken token)
    {
        var result = await callers.Sdk.CommitAsync(request, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(ErrorCode.ResourceExhausted.ToString());
        await Assert.That(result.Problem.Detail).IsEqualTo("The queue dead-letter sublimit is exhausted.");
        await QueueLifecyclePublicRoutes.RefusedAsync(callers, state.Owner, request, ErrorCode.ResourceExhausted, token);
        var reply = await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.ResourceExhausted, true);
        var envelope = reply.StructuredContent
            ?? throw new InvalidOperationException(McpCallerProtocol.MissingStructuredResult);
        await Assert.That(envelope.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString())
            .IsEqualTo("The operation exceeds the available resource capacity.");
        var original = JsonSerializer.SerializeToElement(result.Problem, JsonDefaults.Options);
        state.Refusals.Add((request, original));
    }

    internal static async Task HistoryAsync(RequestCqrsClusterCallers callers,
        QueueByteSublimitPublicState state, CancellationToken token)
    {
        await QueueLifecyclePublicRoutes.ReplayAsync(callers, state.Owner, token);
        foreach (var original in state.Refusals)
        {
            var actual = await callers.Sdk.CommitAsync(original.Request, token);
            await Assert.That(actual.IsFailed).IsTrue();
            await Assert.That(actual.Value).IsNull();
            await Assert.That(JsonDefaults.Serialize(actual.Problem)
                .SequenceEqual(JsonDefaults.Serialize(original.Problem))).IsTrue();
            await QueueLifecyclePublicRoutes.RefusedAsync(callers, state.Owner,
                original.Request, ErrorCode.ResourceExhausted, token);
        }
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(
            await callers.Sdk.ReceiveAsync(state.Request, token), ErrorCode.StaleLease);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, state.Request, token), ErrorCode.StaleLease, true);
        foreach (var action in new[] { DeliveryAction.Ack, DeliveryAction.Nack, DeliveryAction.Renew })
        {
            var old = new DeliveryCommand(Guid.NewGuid(), state.Owner.Lane, state.Delivery.Token, action);
            await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(await callers.Sdk.CompleteAsync(old, token), ErrorCode.StaleLease);
            await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(
                McpCallerTools.MessagesComplete, old, token), ErrorCode.StaleLease, true);
        }
    }

    internal static MessageMetadata Pending()
        => new(First, MessageState.PendingDeadLetter, 1, 3L, 1L, null, null,
            LeaseVersion: 1L, SafeFailureCode: "AttemptsExhausted");

    internal static MessageMetadata ReadySecond()
        => new(Second, MessageState.Ready, 0, 1L, 2L, null, null);

    internal static MessageMetadata ReadyFirst()
        => new(First, MessageState.Ready, 0, 4L, 3L, null, null, LeaseVersion: 2L, DeliveryGeneration: 2L);
}
