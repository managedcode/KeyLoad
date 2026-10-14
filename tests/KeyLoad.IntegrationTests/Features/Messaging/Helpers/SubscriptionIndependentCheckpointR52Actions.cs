using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionIndependentCheckpointR52Actions
{
    internal static async Task<ReceiveSubscriptionResult> ClaimAsync(SubscriptionIndependentCheckpointR52State state,
        SubscriptionRef group, long[] positions, KeyLoadClient sdk, McpOfficialClient mcp,
        KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        var request = SubscriptionPhasePublicAssertions.Receive(group);
        var result = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveSubscriptionAsync(request, token));
        SetIssued(state, group, positions.Last());
        await ClaimResultAsync(state, request, result, positions);
        state.Claims.Add((request, result, state.PolicyEpoch));
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, request, token))).Value, result);
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        return result;
    }

    private static void SetIssued(SubscriptionIndependentCheckpointR52State state, SubscriptionRef group, long issued)
    {
        if (group == state.Seed.Independent)
        { state.Issued = issued; }
        else
        { state.PrimaryIssued = issued; }
    }

    private static async Task ClaimResultAsync(SubscriptionIndependentCheckpointR52State state,
        ReceiveSubscriptionRequest request, ReceiveSubscriptionResult result, long[] positions)
    {
        await SubscriptionFilterRf3Assertions.EqualAsync(result.Status,
            SubscriptionIndependentCheckpointR52Current.Expected(state, request.Subscription));
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(result.Deliveries.Select(x => x.Event.Position)).IsEquivalentTo(positions, CollectionOrdering.Matching);
        foreach (var delivery in result.Deliveries)
        {
            await SubscriptionFilterRf3Assertions.EqualAsync(delivery.Event,
                state.Source!.Events.Single(x => x.Position == delivery.Event.Position));
            await Assert.That(delivery.Attempt).IsEqualTo(1);
            await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
            await Assert.That(delivery.Token).IsNotEmpty();
        }
        await Assert.That(result.Token.AtomicPartitionId).IsEqualTo(request.Subscription.Source.Partition.AtomicPartitionId);
        await Assert.That(result.Token.Incarnation).IsEqualTo(state.Placement!.Incarnation);
        await Assert.That(result.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(result.Token.Position).IsGreaterThan(0L);
    }

    internal static async Task AckAsync(SubscriptionIndependentCheckpointR52State state, SubscriptionRef group,
        SubscriptionDelivery delivery, long checkpoint, KeyLoadClient sdk, McpOfficialClient mcp,
        KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        var request = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, delivery.Token, DeliveryAction.Ack);
        var result = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteSubscriptionAsync(request, token));
        if (group == state.Seed.Independent)
        { state.Checkpoint = checkpoint; }
        else
        { state.PrimaryCheckpoint = checkpoint; }
        await SubscriptionPhasePublicAssertions.ReceiptAsync(result, request, delivery.Event.Position);
        await Assert.That(result.Token.AtomicPartitionId).IsEqualTo(group.Source.Partition.AtomicPartitionId);
        await Assert.That(result.Token.Incarnation).IsEqualTo(state.Placement!.Incarnation);
        await Assert.That(result.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(result.Token.Position).IsGreaterThan(0L);
        await Assert.That(result.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        state.Receipts.Add((request, result, state.PolicyEpoch));
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsComplete, request, token))).Value, result);
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
    }

    internal static async Task SharedHealthyAsync(SubscriptionIndependentCheckpointR52State state,
        KeyLoadClient sdk, McpOfficialClient mcp, KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        var publish = new CommandRequest(Guid.NewGuid(), state.Seed.Group.Source.Partition,
            [new PublishTopic(state.Seed.Group.Source.Resource,
                [new(SubscriptionIndependentCheckpointR52Protocol.HealthyEvent,
                    SubscriptionFilterRf3Protocol.Changed, SubscriptionIndependentCheckpointR52Protocol.Payload)])]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(publish, token));
        await Assert.That(receipt.CommandId).IsEqualTo(publish.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(publish.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(state.Placement!.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SubscriptionFilterRf3Assertions.EqualAsync(receipt.Mutations.ToArray(), new MutationReceipt[]
        { new(SubscriptionIndependentCheckpointR52Protocol.PublishKind, state.Seed.Group.Source.Resource,
            SubscriptionIndependentCheckpointR52Protocol.HealthyEvent, 6L) });
        state.HealthyPublish = publish;
        state.HealthyReceipt = receipt;
        state.Tail = 6;
        await SubscriptionIndependentCheckpointR52Current.CaptureAsync(state, admin, official, token);
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, publish, token))).Value, receipt);
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        foreach (var group in new[] { state.Seed.Group, state.Seed.Independent })
        {
            var claimed = await ClaimAsync(state, group, [6L], sdk, mcp, admin, official, token);
            await AckAsync(state, group, claimed.Deliveries.Single(), 6, sdk, mcp, admin, official, token);
            var emptyRequest = SubscriptionPhasePublicAssertions.Receive(group);
            var empty = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveSubscriptionAsync(emptyRequest, token));
            await Assert.That(empty.RequestId).IsEqualTo(emptyRequest.RequestId);
            await Assert.That(empty.Deliveries).IsEmpty();
            await SubscriptionFilterRf3Assertions.EqualAsync(empty.Status,
                SubscriptionIndependentCheckpointR52Current.Expected(state, group));
            await Assert.That(empty.Token.AtomicPartitionId).IsEqualTo(group.Source.Partition.AtomicPartitionId);
            await Assert.That(empty.Token.Incarnation).IsEqualTo(state.Placement!.Incarnation);
            await Assert.That(empty.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
            await Assert.That(empty.Token.Position).IsGreaterThan(0L);
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
            await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(
                await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, emptyRequest, token))).Value, empty);
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        }
    }
}
