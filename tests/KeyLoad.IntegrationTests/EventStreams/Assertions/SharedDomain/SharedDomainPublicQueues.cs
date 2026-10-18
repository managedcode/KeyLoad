using KeyLoad.Client;
using KeyLoad.RecoveryTests.Features.EventStreams;
using KeyLoad.IntegrationTests.Features.ClientApi;
using static KeyLoad.RecoveryTests.Features.EventStreams.SharedDomainProtocol;
using static KeyLoad.IntegrationTests.Features.EventStreams.SharedDomainPublicProtocol;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class SharedDomainPublicQueues
{
    internal static async Task RequireAsync(KeyLoadClient sdk, McpOfficialClient official,
        SharedDomainPublicState state, SharedDomainScope scope, CancellationToken token)
    {
        if (scope.Queue is not { } queue)
        {
            return;
        }
        var first = scope.Partition.TransactionDomainId == FirstDomain;
        var processed = first && state.Processed;
        var metadata = new MessageMetadata(Item, processed ? MessageState.Acked : MessageState.Ready,
            processed ? FirstAttempt : NoAttempts, processed ? AckVersion : FirstRevision, FirstRevision, null, null,
            LeaseVersion: processed ? FirstRevision : NoRevision);
        if (first && state.Delivery is { } delivery && !processed)
        {
            metadata = metadata with
            {
                State = MessageState.Leased,
                Attempts = FirstAttempt,
                StateVersion = SecondRevision,
                LeaseOwner = state.Identity.Principal.Id,
                LeaseVersion = FirstRevision,
                LeaseUntil = delivery.LeaseUntil
            };
        }
        await OneAsync(sdk, official, new(scope.Partition, queue), Item,
            new(metadata, processed ? null : scope.Payload, processed ? null : Headers), token);
        await OneAsync(sdk, official, new(scope.Partition, queue), Processed,
            processed ? Ready(Processed, scope.Payload, SecondRevision) : null, token);
        await OneAsync(sdk, official, new(scope.Partition, queue), Healthy,
            state.HealthyWritten ? Ready(Healthy, scope.Payload, processed ? AckVersion : SecondRevision) : null, token);
        await OneAsync(sdk, official, new(scope.Partition, queue), Refused, null, token);
    }

    private static MessageInspection Ready(string id, string payload, long sequence)
        => new(new(id, MessageState.Ready, NoAttempts, FirstRevision, sequence, null, null), payload, Headers);

    private static async Task OneAsync(KeyLoadClient sdk, McpOfficialClient official, QueueLaneRef lane,
        string id, MessageInspection? expected, CancellationToken token)
    {
        var request = new InspectMessageRequest(lane, id);
        await SharedDomainPublicValues.EqualAsync(await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(request, token)), expected);
        await SharedDomainPublicValues.EqualAsync((await PublicReadAssertions.MessageAsync(await official.CallAsync(
            McpCallerTools.MessagesInspect, request, token))).Value, expected);
    }
}
