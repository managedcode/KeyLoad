using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationSeed
{
    internal static async Task<QueueViewLeaseIsolationState> RunAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers root, CancellationToken token)
    {
        var scenario = await SqlModelViewClusterScenario.CreateAsync(root.Sdk, token);
        await QueueViewLeaseIsolationReceipts.SeedAsync(scenario);
        var reader = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition, SqlModelViewClusterScenario.Queue,
            Capability.Query | Capability.QueueInspect, token);
        return new(scenario, reader);
    }

    internal static async Task ClaimAsync(RequestCqrsClusterCallers root, QueueViewLeaseIsolationState state, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Scenario.Lane,
            MaxMessages: QueueViewLeaseIsolationProtocol.One, LeaseSeconds: QueueViewLeaseIsolationProtocol.LeaseSeconds);
        var result = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReceiveAsync(request, token));
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(result.Token.Position).IsGreaterThan((long)QueueViewLeaseIsolationProtocol.Empty);
        await SqlClusterProtocol.EqualAsync(new CommitToken(result.Token.Incarnation, state.Scenario.Partition.AtomicPartitionId,
            result.Token.Position), result.Token);
        await Assert.That(result.Deliveries.Length).IsEqualTo(QueueViewLeaseIsolationProtocol.One);
        var delivery = result.Deliveries.Single();
        await Assert.That(delivery.Id).IsEqualTo(SqlModelViewClusterScenario.FirstMessageId);
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
