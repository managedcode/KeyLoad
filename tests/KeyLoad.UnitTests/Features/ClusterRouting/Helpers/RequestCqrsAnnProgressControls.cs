using System.Runtime.CompilerServices;
using KeyLoad.Clustering;
using KeyLoad.UnitTests.Features.Messaging;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsAnnProgressControls
{
    private const int ZeroReplay = 0;
    private const int RepeatedReplay = 2;

    // Controlled native CQRS grammar inputs reuse the genuine Build reply; they execute no ANN owner work.
    internal static async Task RunAsync(RequestCqrsClusterFixture fixture, AnnMaintenanceRequest request,
        GrainOperationReply genuineReply, CancellationToken token)
    {
        await Assert.That(request.Mode).IsEqualTo(AnnMaintenanceMode.Build);
        await Assert.That(genuineReply.Error).IsNull();
        await Assert.That(genuineReply.SafeDetail).IsNull();
        await RequireAsync(fixture, request, genuineReply, ZeroReplay, token);
        await RequireAsync(fixture, request, genuineReply, RepeatedReplay, token);
    }

    private static async Task RequireAsync(RequestCqrsClusterFixture fixture, AnnMaintenanceRequest request,
        GrainOperationReply genuineReply, int replayCount, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var cut = fixture.Database.Store.Position;
        var id = Guid.NewGuid();
        var signed = fixture.Codec.CreateCommand(id, RequestCqrsAnnTestData.Principal,
            OperationKind.MaintainAnnIndex, request.CommandId, NativeSerialization.Serialize(request));
        var observed = new RequestCqrsProducerObservation();
        var frames = new List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>();
        var serializer = fixture.Cluster.ServiceProvider
            .GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>();
        var stream = NativeCqrsStreamLifetime.RunWithPurpose(
            execution => RequestCqrsAnnProducer.Run(id, genuineReply, null, observed,
                fixture.RoutingOptions.Value.MaximumTotalFrames, execution, replayCount),
            serializer, id, fixture.Clock, observed.MarkActivationSettled, fixture.RoutingOptions, null,
            new NativeCqrsStreamPurpose(() => fixture.Codec.VerifyRequest(signed, id)), token);
        var reply = await GrainRequestStreamConsumer.DrainWithPurposeAsync(
            execution => ObserveAsync(stream, frames, execution), serializer, id, fixture.Clock, fixture.RoutingOptions,
            new NativeCqrsStreamPurpose(() => fixture.Codec.VerifyRequest(signed, id)), token);
        await observed.ProducerSettled.Task.WaitAsync(token);
        await observed.ActivationSettled.Task.WaitAsync(token);
        await Assert.That(observed.ProducerSettlementCount).IsEqualTo(RequestCqrsAnnTestData.OneSettlement);
        await Assert.That(observed.ActivationSettlementCount).IsEqualTo(RequestCqrsAnnTestData.OneSettlement);
        await RequestCqrsAnnAssertions.FramesAsync(frames, request, id);
        await Assert.That(frames.Count(frame => frame.Kind == CqrsStreamChunkKind.Progress
            && frame.ProgressResult!.Value.Value!.AnnPhase == AnnMaintenancePhase.Replay)).IsEqualTo(replayCount);
        await Assert.That(reply.Payload.Span.SequenceEqual(genuineReply.Payload.Span)).IsTrue();
        await Assert.That(reply.Error).IsEqualTo(genuineReply.Error);
        await Assert.That(reply.SafeDetail).IsEqualTo(genuineReply.SafeDetail);
        await Assert.That(frames.Last().Final!.Value.Value!.Payload.Span.SequenceEqual(genuineReply.Payload.Span)).IsTrue();
        await Assert.That(frames.Last().Final!.Value.Value!.Error).IsEqualTo(genuineReply.Error);
        await Assert.That(frames.Last().Final!.Value.Value!.SafeDetail).IsEqualTo(genuineReply.SafeDetail);
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store).AsSpan().SequenceEqual(image)).IsTrue();
    }

    private static async IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ObserveAsync(
        IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> stream,
        List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> frames,
        [EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var frame in stream.WithCancellation(token))
        {
            frames.Add(frame);
            yield return frame;
        }
    }
}
