using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationSeed
{
    internal static async Task<QueueViewLeaseIsolationState> RunAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers root, CancellationToken token)
    {
        var scenario = await SqlModelViewRf3Scenario.CreateAsync(root.Sdk, token);
        await QueueViewLeaseIsolationReceipts.SeedAsync(root, scenario, token);
        var reader = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition, SqlModelViewRf3Scenario.Queue,
            Capability.Query | Capability.QueueInspect, token);
        return new(scenario, reader);
    }

    internal static async Task ClaimAsync(RequestCqrsRf3Callers root, QueueViewLeaseIsolationState state, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Scenario.Lane,
            MaxMessages: QueueViewLeaseIsolationProtocol.One, LeaseSeconds: QueueViewLeaseIsolationProtocol.LeaseSeconds);
        var result = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReceiveAsync(request, token));
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
            new(QueueViewLeaseIsolationProtocol.One, state.Scenario.Partition), token));
        await Assert.That(result.Token.Position).IsGreaterThan((long)QueueViewLeaseIsolationProtocol.Empty);
        await SqlRf3Protocol.EqualAsync(new CommitToken(placement.Incarnation, state.Scenario.Partition.AtomicPartitionId,
            result.Token.Position, placement.PlacementEpoch), result.Token);
        await Assert.That(result.Deliveries.Length).IsEqualTo(QueueViewLeaseIsolationProtocol.One);
        var delivery = result.Deliveries.Single();
        await Assert.That(delivery.Id).IsEqualTo(SqlModelViewRf3Scenario.FirstMessageId);
        await Assert.That(delivery.Attempt).IsEqualTo(QueueViewLeaseIsolationProtocol.One);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(QueueViewLeaseIsolationProtocol.InitialVersion);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(QueueViewLeaseIsolationProtocol.InitialVersion);
        await Assert.That(string.IsNullOrEmpty(delivery.Token)).IsFalse();
        await Assert.That(delivery.PayloadJson == QueueViewLeaseIsolationProtocol.FirstPayload
            && delivery.HeadersJson == QueueViewLeaseIsolationProtocol.FirstHeaders).IsTrue();
        state.Claim = request;
        state.OriginalDelivery = result;
        await QueueViewLeaseIsolationImages.AssertLeasedAsync(root, state, token);
    }
}
