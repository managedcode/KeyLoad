using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventMessageSensitivePublicContinuation
{
    internal static async Task RepairAsync(RequestCqrsRf3Callers root,
        RequestCqrsRf3Callers caller, EventMessageSensitivePublicState state, CancellationToken token)
    {
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(),
            state.Worker with { Revoked = true, PolicyEpoch = EventMessageSensitivePublicProtocol.RevokedEpoch }, token));
        var request = EventMessageSensitivePublicOperations.Receive(state, Guid.NewGuid(), true);
        await EventMessageSensitivePublicRefusals.ReceiveAsync(caller, state, request, ErrorCode.Unauthenticated, token);
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), state.Worker with
        { PolicyEpoch = EventMessageSensitivePublicProtocol.RepairedEpoch, FieldGrants = [EventMessageSensitivePublicProtocol.GrantAfter] }, token));
    }

    internal static async Task HealthyAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers worker,
        RequestCqrsRf3Callers inspector, EventMessageSensitivePublicState state, CancellationToken token)
    {
        if (!state.Subscription)
        {
            await EventMessageSensitivePublicOperations.CompleteAsync(worker, state, state.Original, DeliveryAction.Nack, token);
            await EventMessageSensitivePublicReads.RedactedAsync(inspector, state, true, token);
            var request = new InspectMessageRequest(state.Lane, EventMessageSensitivePublicProtocol.Message);
            var parked = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.InspectAsync(request, token));
            await Assert.That(parked!.Metadata.State).IsEqualTo(MessageState.DeadLettered);
            var command = new CommandRequest(Guid.NewGuid(), state.Partition,
                [new RedriveQueueMessage(state.Resource.Name, request.Id, parked.Metadata.StateVersion, parked.Metadata.DeliveryGeneration)]);
            state.Commands.Add((command, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token))));
        }
        state.HealthyId = Guid.NewGuid();
        state.HealthyRequest = EventMessageSensitivePublicOperations.Receive(state, state.HealthyId, true);
        state.Healthy = await EventMessageSensitivePublicOperations.ReceiveAsync(state.DataAuthority ? root : worker,
            state, state.HealthyRequest, QueueLifecyclePublicProtocol.Sdk, token);
        await EventMessageSensitivePublicAssertions.FullAsync(state.Healthy, state);
    }

    internal static async Task FinishAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers caller,
        EventMessageSensitivePublicState state, CancellationToken token)
    {
        await EventMessageSensitivePublicOperations.CompleteAsync(caller, state, state.Healthy, DeliveryAction.Ack, token);
        await EventMessageSensitivePublicAssertions.ReceiptReplayAsync(root, caller, state, token);
        foreach (var route in QueueLifecyclePublicProtocol.Routes)
        {
            var request = EventMessageSensitivePublicOperations.Receive(state, Guid.NewGuid(), true);
            var empty = await EventMessageSensitivePublicOperations.ReceiveAsync(caller, state, request, route, token);
            if (empty is ReceiveSubscriptionResult subscription)
            { await Assert.That(subscription.Deliveries).IsEmpty(); }
            else
            { await Assert.That(((ReceiveResult)empty).Deliveries).IsEmpty(); }
        }
    }
}
