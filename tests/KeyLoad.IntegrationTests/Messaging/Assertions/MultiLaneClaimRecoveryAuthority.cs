using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class MultiLaneClaimRecoveryAuthority
{
    internal static async Task ClaimAsync(RequestCqrsRf3Callers administrator, ReceiveRequest request,
        ReceiveResult actual, MessageInspection committed, string principalId, string id, string payload,
        string headers, long readySequence, CancellationToken token)
    {
        var delivery = await Assert.That(actual.Deliveries).HasSingleItem();
        await Assert.That(delivery.Token).IsNotEqualTo(string.Empty);
        var expectedDelivery = new Delivery(id, payload, headers, delivery.Token,
            MultiLaneClaimRecoveryProtocol.FirstAttempt, committed.Metadata.LeaseUntil!.Value,
            MultiLaneClaimRecoveryProtocol.InitialVersion, MultiLaneClaimRecoveryProtocol.InitialVersion);
        var expectedToken = await TokenAsync(administrator, actual.Token, request.Lane.Partition, token);
        await MultiLaneReceiveCancellationAssertions.EqualAsync(actual,
            new ReceiveResult(request.RequestId, [expectedDelivery], expectedToken));
        var expectedImage = new MessageInspection(new(id, MessageState.Leased,
            MultiLaneClaimRecoveryProtocol.FirstAttempt, MultiLaneClaimRecoveryProtocol.ClaimedVersion,
            readySequence, null, null, principalId, MultiLaneClaimRecoveryProtocol.InitialVersion,
            expectedDelivery.LeaseUntil), payload, headers);
        await MultiLaneReceiveCancellationAssertions.EqualAsync(committed, expectedImage);
        await ImageAsync(administrator, request.Lane, id, expectedImage, token);
    }

    internal static async Task AckAsync(RequestCqrsRf3Callers administrator, DeliveryCommand command,
        CommitReceipt actual, string id, long readySequence, CancellationToken token)
    {
        var expectedToken = await TokenAsync(administrator, actual.Token, command.Lane.Partition, token);
        await MultiLaneReceiveCancellationAssertions.EqualAsync(actual, new CommitReceipt(command.CommandId,
            expectedToken, [new(nameof(DeliveryAction.Ack), command.Lane.Queue, id,
                MultiLaneClaimRecoveryProtocol.SettledVersion)], DurabilityProfile.QuorumProcessDurable));
        var image = new MessageInspection(new(id, MessageState.Acked,
            MultiLaneClaimRecoveryProtocol.FirstAttempt, MultiLaneClaimRecoveryProtocol.SettledVersion,
            readySequence, null, null, LeaseVersion: MultiLaneClaimRecoveryProtocol.InitialVersion), null, null);
        await ImageAsync(administrator, command.Lane, id, image, token);
    }

    internal static async Task ImageAsync(RequestCqrsRf3Callers administrator, QueueLaneRef lane,
        string id, MessageInspection expected, CancellationToken token)
    {
        var actual = await PublicReadAssertions.MessageAsync(await administrator.Sdk.InspectAsync(
            new(lane, id), token).ConfigureAwait(false)).ConfigureAwait(false);
        await MultiLaneReceiveCancellationAssertions.EqualAsync(actual, expected);
    }

    private static async Task<CommitToken> TokenAsync(RequestCqrsRf3Callers administrator,
        CommitToken actual, PartitionRef partition, CancellationToken token)
    {
        var placement = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadAtomicPartitionPlacementAsync(
            new(MultiLaneClaimRecoveryProtocol.PlacementVersion, partition), token).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(placement.Partition).IsEqualTo(partition);
        await Assert.That(actual.Position).IsGreaterThan(MultiLaneClaimRecoveryProtocol.MinimumPosition);
        return new(placement.Incarnation, partition.AtomicPartitionId, actual.Position, placement.PlacementEpoch);
    }
}
