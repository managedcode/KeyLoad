using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueuePolicyEpochRf3Delivery
{
    internal static async Task<QueuePolicyEpochRf3Known[]> ExecuteAsync(TwoRf3MembershipWave wave,
        QueuePolicyEpochRf3Seed seed, string id, long epoch, CancellationToken token)
    {
        var delivery = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            seed.Credential, async callers =>
        {
            var command = new ReceiveRequest(Guid.NewGuid(), seed.Lane);
            var actual = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(command, token));
            var selected = await Assert.That(actual.Deliveries).HasSingleItem();
            await Assert.That(selected.Id).IsEqualTo(id);
            await Assert.That(selected.PayloadJson).IsEqualTo(QueuePolicyEpochRf3Seed.Payload);
            await Assert.That(selected.HeadersJson).IsEqualTo(QueuePolicyEpochRf3Seed.Headers);
            await Assert.That(selected.Attempt).IsEqualTo(1);
            await Assert.That(selected.LeaseVersion).IsEqualTo(1L);
            await SqlRf3Protocol.EqualAsync(actual, (await McpCallerAssertions.SuccessAsync<ReceiveResult>(
                await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, command, token))).Value);
            return selected;
        }, token);
        var renew = new DeliveryCommand(Guid.NewGuid(), seed.Lane, delivery.Token, DeliveryAction.Renew);
        var renewed = await QueuePolicyEpochRf3Routes.CompleteAsync(wave, seed, renew, token);
        var ack = new DeliveryCommand(Guid.NewGuid(), seed.Lane, delivery.Token, DeliveryAction.Ack);
        var acknowledged = await QueuePolicyEpochRf3Routes.CompleteAsync(wave, seed, ack, token);
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            seed.Credential, async callers =>
        {
            var actual = await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(new(seed.Lane, id), token));
            await Assert.That(actual).IsNotNull();
            await Assert.That(actual!.Metadata.State).IsEqualTo(MessageState.Acked);
            await Assert.That(actual.Metadata.Id).IsEqualTo(id);
            await Assert.That(actual.Metadata.Attempts).IsEqualTo(1);
            await Assert.That(actual.PayloadJson).IsNull();
            await Assert.That(actual.HeadersJson).IsNull();
            await SqlRf3Protocol.EqualAsync(new MutationReceipt(nameof(DeliveryAction.Ack), seed.Lane.Queue,
                id, actual.Metadata.StateVersion), acknowledged.Mutations.Single());
            await SqlRf3Protocol.EqualAsync(new MutationReceipt(nameof(DeliveryAction.Renew), seed.Lane.Queue,
                id, actual.Metadata.StateVersion - 1), renewed.Mutations.Single());
            return true;
        }, token);
        return [new(renew, epoch, renewed, null), new(ack, epoch, acknowledged, null)];
    }

    internal static Task<bool> PublishHealthyAsync(TwoRf3MembershipWave wave, string administrator,
        QueuePolicyEpochRf3Seed seed, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            administrator, async callers =>
        {
            var command = new CommandRequest(Guid.NewGuid(), seed.Lane.Partition,
                [new EnqueueMessage(seed.Lane.Queue, QueuePolicyEpochRf3Seed.Healthy,
                    QueuePolicyEpochRf3Seed.Payload, QueuePolicyEpochRf3Seed.Headers)]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token));
            await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
            return true;
        }, token);
}
