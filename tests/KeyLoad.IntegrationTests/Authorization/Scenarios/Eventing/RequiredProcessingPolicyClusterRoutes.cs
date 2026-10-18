using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RequiredProcessingPolicyClusterRoutes
{
    internal static Task<CommitReceipt> ProcessAsync(RequestCqrsClusterCallers reader, RequiredProcessingPolicyClusterState state,
        ProcessingRequest request, int route, CancellationToken token)
        => QueueLifecyclePublicRoutes.CallAsync(reader, route, state.Partition, RequiredProcessingPolicyClusterState.Tool, request,
            () => reader.Sdk.CommitProcessingAsync(request, token), token);
    internal static async Task<Delivery> ReceiveAsync(RequestCqrsClusterCallers reader, RequiredProcessingPolicyClusterState state, string id, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: state.Resource.QueuePolicy.MaxLeaseSeconds);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReceiveAsync(request, token));
        await Assert.That(actual.Deliveries.Length).IsEqualTo(1);
        var delivery = actual.Deliveries[0];
        await Assert.That(delivery.Token).IsNotEmpty();
        await Assert.That(delivery.LeaseUntil).IsGreaterThan(TimeProvider.System.GetUtcNow());
        await SqlClusterProtocol.EqualAsync(new Delivery(id, state.Payload, state.Headers, delivery.Token, 1L, delivery.LeaseUntil, 1, 1L), delivery);
        await Assert.That(actual.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(actual.Token.Incarnation).IsEqualTo(state.Producers[0].Receipt.Token.Incarnation);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(state.Partition.AtomicPartitionId);
        await Assert.That(actual.PersistedCopies).IsEqualTo(3);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        state.Deliveries.Add(delivery);
        return delivery;
    }
    internal static async Task<CommitReceipt> CompleteAsync(RequestCqrsClusterCallers reader,
        RequiredProcessingPolicyClusterState state, ProcessingRequest request, string id, CancellationToken token)
    {
        var receipt = await ProcessAsync(reader, state, request, 0, token);
        await RequiredProcessingPolicyClusterValues.ReceiptAsync(state, request.CommandId,
            [new("putDocument", RequiredProcessingPolicyClusterState.Derived, id, 1L), new("Ack", RequiredProcessingPolicyClusterState.Queue, id, 3L)], receipt);
        return receipt;
    }
}
