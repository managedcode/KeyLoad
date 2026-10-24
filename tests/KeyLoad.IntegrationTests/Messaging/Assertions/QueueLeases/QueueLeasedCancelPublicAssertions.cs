using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueLeasedCancelPublicAssertions
{
    internal const string Target = "leased-cancel-target";
    internal const string Sibling = "leased-cancel-sibling";
    internal const string Replacement = "leased-cancel-replacement";
    internal const string Witness = "leased-cancel-witness";

    internal static async Task ImagesAsync(RequestCqrsClusterCallers callers,
        QueueLeasedCancelPublicState state, bool cancelled, CancellationToken token)
    {
        var owner = state.Owner;
        var target = cancelled ? new MessageInspection(new MessageMetadata(Target, MessageState.Cancelled,
            1, 3L, 1L, null, null, LeaseVersion: 2L, DeliveryGeneration: 2L), null, null) : state.Leased;
        await MessageAsync(callers, owner, Target, target, token);
        await MessageAsync(callers, owner, Sibling, new(new(Sibling, MessageState.Ready, 0, 1L, 2L, null, null),
            QueueLifecyclePublicProtocol.CanonicalPayload, QueueLifecyclePublicProtocol.Headers), token);
        var witness = await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(
            new(owner.Partition, QueueLifecyclePublicProtocol.Collection, Witness), token));
        await Assert.That(witness!.Json).IsEqualTo(QueueLifecyclePublicProtocol.Payload);
        await Assert.That(witness.Revision).IsEqualTo(1L);
    }

    internal static async Task MessageAsync(RequestCqrsClusterCallers callers, QueueLifecyclePublicState owner,
        string id, MessageInspection? expected, CancellationToken token)
    {
        var request = new InspectMessageRequest(owner.Lane, id);
        var actual = await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(request, token));
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(JsonDefaults.Serialize((await PublicReadAssertions.MessageAsync(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesInspect, request, token))).Value)
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task<JsonElement> RefusedAsync(RequestCqrsClusterCallers callers, QueueLifecyclePublicState owner,
        CommandRequest command, ErrorCode code, CancellationToken token)
    {
        var result = await callers.Sdk.CommitAsync(command, token);
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(result, code);
        await Assert.That(result.Value).IsNull();
        await QueueLifecyclePublicRoutes.RefusedAsync(callers, owner, command, code, token);
        return JsonSerializer.SerializeToElement(result.Problem, JsonDefaults.Options);
    }

    internal static async Task OriginalAsync(RequestCqrsClusterCallers callers, QueueLeasedCancelPublicState state,
        CancellationToken token)
    {
        await QueueLifecyclePublicRoutes.ReplayAsync(callers, state.Owner, token);
        foreach (var saved in state.Failures)
        {
            var result = await callers.Sdk.CommitAsync(saved.Command, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await Assert.That(JsonDefaults.Serialize(result.Problem)
                .SequenceEqual(JsonDefaults.Serialize(saved.Problem))).IsTrue();
            await QueueLifecyclePublicRoutes.RefusedAsync(callers, state.Owner, saved.Command,
                saved.Code, token);
        }
        var original = await callers.Sdk.ReceiveAsync(state.Claim, token);
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(original, ErrorCode.StaleLease);
        await Assert.That(original.Value).IsNull();
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive,
            state.Claim, token), ErrorCode.StaleLease, true);
        foreach (var action in new[] { DeliveryAction.Ack, DeliveryAction.Nack, DeliveryAction.Renew })
        {
            var command = new DeliveryCommand(Guid.NewGuid(), state.Owner.Lane, state.Claimed.Deliveries.Single().Token, action);
            await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(await callers.Sdk.CompleteAsync(command, token), ErrorCode.StaleLease);
            await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.MessagesComplete,
                command, token), ErrorCode.StaleLease, true);
        }
    }

    internal static async Task FinalImagesAsync(RequestCqrsClusterCallers callers,
        QueueLeasedCancelPublicState state, bool healthy, CancellationToken token)
    {
        await MessageAsync(callers, state.Owner, Target, new(new(Target, MessageState.Cancelled,
            1, 3L, 1L, null, null, LeaseVersion: 2L, DeliveryGeneration: 2L), null, null), token);
        foreach (var expected in new[] { (Sibling, 2L), (Replacement, 3L) })
        {
            await MessageAsync(callers, state.Owner, expected.Item1, new(new(expected.Item1, MessageState.Acked,
                1, 3L, expected.Item2, null, null, LeaseVersion: 1L), null, null), token);
        }
        if (healthy)
        {
            await MessageAsync(callers, state.Owner, QueueLifecyclePublicProtocol.Healthy,
                new(new(QueueLifecyclePublicProtocol.Healthy, MessageState.Acked,
                    1, 3L, 4L, null, null, LeaseVersion: 1L), null, null), token);
        }
    }

}
