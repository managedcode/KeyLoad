using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventMessageSensitivePublicOperations
{
    internal static object Receive(EventMessageSensitivePublicState state, Guid id, bool healthy)
        => state.Subscription
            ? new ReceiveSubscriptionRequest(id, healthy ? state.HealthyGroup : state.OriginalGroup, LeaseSeconds: new SubscriptionPolicy().MaxLeaseSeconds)
            : new ReceiveRequest(id, state.Lane, LeaseSeconds: state.Resource.QueuePolicy.MaxLeaseSeconds);

    internal static async Task<object> ReceiveAsync(RequestCqrsRf3Callers callers, EventMessageSensitivePublicState state,
        object request, int route, CancellationToken token)
    {
        if (request is ReceiveSubscriptionRequest subscription)
        {
            return await QueueLifecyclePublicRoutes.CallAsync(callers, route, state.Partition, McpCallerTools.SubscriptionsReceive,
                subscription, () => callers.Sdk.ReceiveSubscriptionAsync(subscription, token), token);
        }
        var queue = (ReceiveRequest)request;
        return await QueueLifecyclePublicRoutes.CallAsync(callers, route, state.Partition, McpCallerTools.MessagesReceive,
            queue, () => callers.Sdk.ReceiveAsync(queue, token), token);
    }

    internal static async Task GroupAsync(RequestCqrsRf3Callers root, EventMessageSensitivePublicState state,
        bool healthy, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var request = new ConfigureSubscriptionRequest(id, healthy ? state.HealthyGroup : state.OriginalGroup, new(state.Worker.Id));
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureSubscriptionAsync(request, token));
    }

    internal static async Task PolicyAsync(RequestCqrsRf3Callers root, EventMessageSensitivePublicState state,
        bool required, CancellationToken token)
    {
        var policy = new SensitiveFieldPolicy(EventMessageSensitivePublicProtocol.Path,
            EventMessageSensitivePublicProtocol.Classification, EventMessageSensitivePublicProtocol.GrantAfter, RequiredForProcessing: required);
        var definition = state.Header ? state.Resource with { HeaderPolicies = [policy] } : state.Resource with { FieldPolicies = [policy] };
        var request = new ConfigureResourceRequest(state.Partition.TenantId, state.Partition.DatabaseId, definition)
        { ExpectedSchemaVersion = state.Resource.SchemaVersion };
        state.Resource = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), request, token));
    }

    internal static async Task<CommitReceipt> CompleteAsync(RequestCqrsRf3Callers caller, EventMessageSensitivePublicState state,
        object received, DeliveryAction action, CancellationToken token)
    {
        var id = Guid.NewGuid();
        object request;
        CommitReceipt receipt;
        if (received is ReceiveSubscriptionResult subscription)
        {
            request = new SubscriptionDeliveryCommand(id, state.HealthyGroup, subscription.Deliveries.Single().Token, action);
            receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CompleteSubscriptionAsync((SubscriptionDeliveryCommand)request, token));
        }
        else
        {
            request = new DeliveryCommand(id, state.Lane, ((ReceiveResult)received).Deliveries.Single().Token, action);
            receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CompleteAsync((DeliveryCommand)request, token));
        }
        state.Completions.Add((request, receipt));
        return receipt;
    }
}
