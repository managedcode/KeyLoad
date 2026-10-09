using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.ClusterRouting;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class MultiLaneReceiveNativeFlow
{
    internal static Task<GrainOperationReply> ReceiveAsync(RequestCqrsClusterFixture fixture,
        MultiLaneReceiveRequest request, string principal = "root", CancellationToken token = default)
        => InvokeAsync(fixture, OperationKind.ReceiveAcrossLanes, request.RequestId, request, principal, token);

    internal static async Task<GrainOperationReply> InvokeAsync<T>(RequestCqrsClusterFixture fixture,
        OperationKind kind, Guid commandId, T request, string principalId, CancellationToken token)
    {
        var principal = GrainRequestAuthority.Reload(fixture.Database.Database, principalId,
            fixture.Database.Database.EvaluationClock);
        var actor = Guid.NewGuid();
        var signed = fixture.Codec.CreateCommand(actor, principal.Id, kind, commandId,
            NativeSerialization.Serialize(request));
        using var identity = new GrainRequestIdentityScope(fixture.Cluster.ServiceProvider, principal,
            actor, commandId, token, connectionId: fixture.ConnectionId);
        return await GrainRequestStreamConsumer.DrainAsync(
            createStream: cancellation => fixture.Cluster.Client.GetGrain<IConnectionGrain>(fixture.ConnectionId)
                .ExecuteStreamAsync(signed, cancellation),
            serializer: fixture.Cluster.ServiceProvider.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            requestId: actor, clock: fixture.Database.Database.EvaluationClock,
            options: fixture.RoutingOptions, cancellationToken: token);
    }

    internal static MultiLaneReceiveResult Value(GrainOperationReply reply)
        => NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as MultiLaneReceiveResult
            ?? throw new InvalidOperationException("The native request returned no complete multi-lane outcome.");

    internal static QueueLaneRef[] Seed(RequestCqrsClusterFixture fixture, int count)
    {
        var prefix = "multi-" + Guid.NewGuid().ToString("N");
        var lanes = new QueueLaneRef[count];
        for (var index = 0; index < count; index++)
        {
            var queue = prefix + "-" + index;
            fixture.Database.Configure(queue, ResourceKind.WorkQueue);
            fixture.Database.Commit(new EnqueueMessage(queue, "message", "{\"work\":1}", "{\"kind\":\"job\"}"));
            lanes[index] = new(fixture.Database.Partition, queue);
        }
        return lanes;
    }

    internal static async Task CommittedAsync(QueueLaneReceiveOutcome outcome, ReceiveRequest request)
    {
        await Assert.That(outcome.Status).IsEqualTo(QueueLaneReceiveStatus.Committed);
        await Assert.That(outcome.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(outcome.Lane).IsEqualTo(request.Lane);
        await Assert.That(outcome.Error).IsNull();
        await Assert.That(outcome.SafeDetail).IsNull();
        var result = outcome.Result ?? throw new InvalidOperationException("Committed lane has no result.");
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        var delivery = await Assert.That(result.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo("message");
        await Assert.That(delivery.PayloadJson).IsEqualTo("{\"work\":1}");
        await Assert.That(delivery.HeadersJson).IsEqualTo("{\"kind\":\"job\"}");
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        await Assert.That(delivery.Token).IsNotEmpty();
    }
}
