using KeyLoad.Clustering;
using KeyLoad.FunctionalTests.Messaging;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RequestCqrsAnnCheckpointReplay
{
    internal static async Task RunAsync(RequestCqrsHostedRuntimeFixture fixture, AnnMaintenanceRequest request,
        AnnMaintenanceResult result, CancellationToken token)
    {
        var manifest = RequestCqrsAnnResultAssertions.Manifest(fixture, request);
        var original = manifest.CheckpointIntent ?? throw new InvalidOperationException();
        var checkpoint = result.Checkpoint ?? throw new InvalidOperationException();
        var image = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var cut = fixture.Database.Store.Position;
        var id = Guid.NewGuid();
        var envelope = fixture.Codec.CreateCommand(id, RequestCqrsAnnTestData.Principal,
            OperationKind.CommitProjectionBatch, original.CommandId, OrleansSerialization.Serialize(original));
        var principal = fixture.Database.Store.Read(view => fixture.Database.Database.Principal(view,
            RequestCqrsAnnTestData.Principal, fixture.Clock.GetUtcNow()));
        var connection = fixture.Grains.GetGrain<IConnectionGrain>(fixture.ConnectionId);
        using var identity = new GrainRequestIdentityScope(fixture.Services, principal,
            id, original.CommandId, token, connectionId: fixture.ConnectionId);
        var reply = await GrainRequestStreamConsumer.DrainAsync(
            execution => connection.ExecuteStreamAsync(envelope, execution),
            fixture.Services.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            id, fixture.Clock, fixture.RoutingOptions, token);
        await Assert.That(reply.Error).IsNull();
        var replay = OrleansSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as ProjectionBatchResult
            ?? throw new InvalidOperationException();
        await Assert.That(OrleansSerialization.Serialize(replay).AsSpan().SequenceEqual(OrleansSerialization.Serialize(checkpoint))).IsTrue();
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store).AsSpan().SequenceEqual(image)).IsTrue();
    }
}
