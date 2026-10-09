using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3EventingReadOracle
{
    internal static async Task RequireAsync(KeyLoadClient sdk, McpOfficialClient official,
        ClusterRestoreRf3EventingState state, CancellationToken cancellationToken)
    {
        await PagesAsync(sdk, official, state, cancellationToken).ConfigureAwait(false);
        var tail = state.Resumed ? ClusterRestoreRf3EventingProtocol.HealthyPosition : ClusterRestoreRf3EventingProtocol.LastSourcePosition;
        var group = state.Resumed ? new SubscriptionInfo(state.Group, new(PartitionMovementPublicParentRf3Administrator.PrincipalId),
            ClusterRestoreRf3EventingProtocol.ReconciledGeneration, ClusterRestoreRf3EventingProtocol.ReconciledOwnershipEpoch,
            tail, tail, tail, false, null) : ClusterRestoreRf3EventingSourceOracle.Group(state, other: false);
        await FourAsync(group, await McpCallerAssertions.SdkSuccessAsync(await sdk.SubscriptionStatusAsync(state.Group,
            cancellationToken).ConfigureAwait(false)), sdk, official, state.Partition, McpCallerTools.SubscriptionsStatus,
            new GetSubscriptionRequest(state.Group), cancellationToken).ConfigureAwait(false);
        var other = ClusterRestoreRf3EventingSourceOracle.Group(state, other: true) with { TailPosition = tail };
        await FourAsync(other, await McpCallerAssertions.SdkSuccessAsync(await sdk.SubscriptionStatusAsync(state.Other,
            cancellationToken).ConfigureAwait(false)), sdk, official, state.Partition, McpCallerTools.SubscriptionsStatus,
            new GetSubscriptionRequest(state.Other), cancellationToken).ConfigureAwait(false);
        await EffectsAsync(sdk, official, state, cancellationToken).ConfigureAwait(false);
        if (state.Resumed)
        {
            var inspect = new InspectMessageRequest(new(state.Partition, ClusterRestoreRf3EventingProtocol.HealthyQueue),
                ClusterRestoreRf3EventingProtocol.Healthy);
            await FourAsync<MessageInspection?>(state.HealthyMessage, await McpCallerAssertions.SdkSuccessAsync(
                await sdk.InspectAsync(inspect, cancellationToken).ConfigureAwait(false)), sdk, official, state.Partition,
                McpCallerTools.MessagesInspect, inspect, cancellationToken).ConfigureAwait(false);
        }
        var expectedOutbox = state.Resumed ? state.FinalOutbox : state.Outbox;
        await FourAsync(expectedOutbox, await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(state.Partition,
            cancellationToken).ConfigureAwait(false)), sdk, official, state.Partition, ClusterRestoreRf3EventingProtocol.OutboxStatus,
            new GetOutboxStatusRequest(state.Partition), cancellationToken).ConfigureAwait(false);
    }

    private static async Task PagesAsync(KeyLoadClient sdk, McpOfficialClient official,
        ClusterRestoreRf3EventingState state, CancellationToken cancellationToken)
    {
        var request = new ReadEventSourceRequest(state.Source);
        await PageAsync(state, await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadEventSourceAsync(request,
            cancellationToken).ConfigureAwait(false)));
        await PageAsync(state, (await McpCallerAssertions.SuccessAsync<EventSourcePage>(await official.CallAsync(
            McpCallerTools.EventsRead, request, cancellationToken).ConfigureAwait(false))).Value);
        var sql = SqlRf3Protocol.Call(state.Partition, McpCallerTools.EventsRead, request);
        await PageAsync(state, await SqlRf3Protocol.SdkAsync<EventSourcePage>(sdk, sql, cancellationToken));
        await PageAsync(state, await SqlRf3Protocol.McpAsync<EventSourcePage>(official, sql, cancellationToken));
    }

    private static async Task PageAsync(ClusterRestoreRf3EventingState state, EventSourcePage actual)
    {
        await Assert.That(actual.Cursor).IsNotEmpty();
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(state.Page.CutPosition);
        var events = state.Resumed ? state.FinalEvents : state.Page.Events;
        var tail = state.Resumed ? ClusterRestoreRf3EventingProtocol.HealthyPosition : ClusterRestoreRf3EventingProtocol.LastSourcePosition;
        await SqlRf3Protocol.EqualAsync(new EventSourcePage(state.Source, new(tail,
            ClusterRestoreRf3EventingProtocol.FirstPosition, ClusterRestoreRf3EventingProtocol.InitialGeneration),
            events, actual.Cursor, actual.CutPosition, false), actual);
    }

    internal static async Task EffectsAsync(KeyLoadClient sdk, McpOfficialClient official,
        ClusterRestoreRf3EventingState state, CancellationToken cancellationToken)
    {
        var reference = new EntityRef(state.Partition, ClusterRestoreRf3EventingProtocol.Documents,
            ClusterRestoreRf3EventingProtocol.DocumentId);
        await FourAsync<DocumentResult?>(new(reference, ClusterRestoreRf3EventingProtocol.FirstRevision,
            ClusterRestoreRf3EventingProtocol.Json, false, []), await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(
            reference, cancellationToken).ConfigureAwait(false)), sdk, official, state.Partition, McpCallerTools.DocumentsGet,
            new GetDocumentRequest(reference), cancellationToken).ConfigureAwait(false);
        var inspect = new InspectMessageRequest(state.Lane, ClusterRestoreRf3EventingProtocol.MessageId);
        await FourAsync<MessageInspection?>(state.Message, await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            inspect, cancellationToken).ConfigureAwait(false)), sdk, official, state.Partition, McpCallerTools.MessagesInspect,
            inspect, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task FourAsync<T>(T expected, T sdkValue, KeyLoadClient sdk, McpOfficialClient official,
        PartitionRef partition, string tool, object request, CancellationToken cancellationToken)
    {
        await SqlRf3Protocol.EqualAsync(expected, sdkValue);
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<T>(await official.CallAsync(
            tool, request, cancellationToken).ConfigureAwait(false))).Value);
        var sql = SqlRf3Protocol.Call(partition, tool, request, CommandId(request));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<T>(sdk, sql, cancellationToken));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<T>(official, sql, cancellationToken));
    }
    internal static Guid? CommandId(object request) => request switch
    {
        CommandRequest value => value.CommandId,
        SeekSubscriptionRequest value => value.CommandId,
        SetSubscriptionPausedRequest value => value.CommandId,
        ReceiveSubscriptionRequest value => value.RequestId,
        SubscriptionProcessingRequest value => value.CommandId,
        SubscriptionDeliveryCommand value => value.CommandId,
        ReceiveRequest value => value.RequestId,
        DeliveryCommand value => value.CommandId,
        _ => null
    };

}
