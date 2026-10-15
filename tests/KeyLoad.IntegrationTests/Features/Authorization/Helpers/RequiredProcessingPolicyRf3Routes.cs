using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RequiredProcessingPolicyRf3Routes
{
    internal static Task<CommitReceipt> ProcessAsync(RequestCqrsRf3Callers reader, RequiredProcessingPolicyRf3State state,
        ProcessingRequest request, int route, CancellationToken token)
        => QueueLifecyclePublicRoutes.CallAsync(reader, route, state.Partition, RequiredProcessingPolicyRf3State.Tool, request,
            () => reader.Sdk.CommitProcessingAsync(request, token), token);
    internal static async Task<Delivery> ReceiveAsync(RequestCqrsRf3Callers reader, RequiredProcessingPolicyRf3State state, string id, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: state.Resource.QueuePolicy.MaxLeaseSeconds);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReceiveAsync(request, token));
        await Assert.That(actual.Deliveries.Length).IsEqualTo(1);
        var delivery = actual.Deliveries[0];
        await Assert.That(delivery.Token).IsNotEmpty();
        await Assert.That(delivery.LeaseUntil).IsGreaterThan(DateTimeOffset.UtcNow);
        await SqlRf3Protocol.EqualAsync(new Delivery(id, state.Payload, state.Headers, delivery.Token, 1L, delivery.LeaseUntil, 1, 1L), delivery);
        await Assert.That(actual.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(actual.Token.Incarnation).IsEqualTo(state.Producers[0].Receipt.Token.Incarnation);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(state.Partition.AtomicPartitionId);
        await Assert.That(actual.Token.OwnershipEpoch).IsEqualTo(state.Producers[0].Receipt.Token.OwnershipEpoch);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        state.Deliveries.Add(delivery);
        return delivery;
    }
    internal static async Task<CommitReceipt> CompleteAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        RequiredProcessingPolicyRf3State state, ProcessingRequest request, string id, CancellationToken token)
    {
        var receipt = await ProcessAsync(reader, state, request, 0, token);
        await RequiredProcessingPolicyRf3Values.ReceiptAsync(root, state, request.CommandId,
            [new("putDocument", RequiredProcessingPolicyRf3State.Derived, id, 1L), new("Ack", RequiredProcessingPolicyRf3State.Queue, id, 3L)], receipt, token);
        return receipt;
    }
}
