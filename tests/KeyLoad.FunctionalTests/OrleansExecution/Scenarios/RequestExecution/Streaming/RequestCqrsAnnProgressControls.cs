using System.Runtime.CompilerServices;
using KeyLoad.Clustering;
using KeyLoad.FunctionalTests.Messaging;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RequestCqrsAnnProgressControls
{
    private const int RepeatedReplay = 2;

    // Controlled execution CQRS grammar inputs reuse the genuine Build reply; they execute no ANN owner work.
    internal static async Task RunAsync(RequestCqrsHostedRuntimeFixture fixture, AnnMaintenanceRequest request,
        GrainOperationReply genuineReply, CancellationToken token)
    {
        await Assert.That(request.Mode).IsEqualTo(AnnMaintenanceMode.Build);
        await Assert.That(genuineReply.Error).IsNull();
        await Assert.That(genuineReply.SafeDetail).IsNull();
        await RequireAsync(fixture, request, genuineReply, 0, token);
        await RequireAsync(fixture, request, genuineReply, RepeatedReplay, token);
    }

    private static async Task RequireAsync(RequestCqrsHostedRuntimeFixture fixture, AnnMaintenanceRequest request,
        GrainOperationReply genuineReply, int replayCount, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var cut = fixture.Database.Store.Position;
        var id = Guid.NewGuid();
        var envelope = fixture.Codec.CreateCommand(id, RequestCqrsAnnTestData.Principal,
            OperationKind.MaintainAnnIndex, request.CommandId, request);
        var observed = new RequestCqrsProducerObservation();
        var frames = new List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>();
        var serializer = fixture.Services
            .GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>();
        var stream = CqrsStreamLifetime.RunWithPurpose(
            execution => RequestCqrsAnnProducer.Run(id, genuineReply, null, observed,
                execution, replayCount), id, observed.MarkActivationSettled, null,
            new CqrsStreamPurpose(() => fixture.Codec.DecodeRequest(envelope, id)), token);
        var reply = await GrainRequestStreamConsumer.DrainWithPurposeAsync(
            execution => ObserveAsync(stream, frames, execution), id,
            new CqrsStreamPurpose(() => fixture.Codec.DecodeRequest(envelope, id)), token);
        await observed.ProducerSettled.Task.WaitAsync(token);
        await observed.ActivationSettled.Task.WaitAsync(token);
        await Assert.That(observed.ProducerSettlementCount).IsEqualTo(RequestCqrsAnnTestData.OneSettlement);
        await Assert.That(observed.ActivationSettlementCount).IsEqualTo(RequestCqrsAnnTestData.OneSettlement);
        await RequestCqrsAnnAssertions.FramesAsync(frames, request, id);
        await Assert.That(frames.Count(frame => frame.Kind == CqrsStreamChunkKind.Progress
            && frame.ProgressResult!.Value.Value!.AnnPhase == AnnMaintenancePhase.Replay)).IsEqualTo(replayCount);
        var expected = OrleansSerialization.Serialize(GrainReplyFactory.RequireValue(genuineReply));
        await Assert.That(OrleansSerialization.Serialize(GrainReplyFactory.RequireValue(reply)).AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(reply.Error).IsEqualTo(genuineReply.Error);
        await Assert.That(reply.SafeDetail).IsEqualTo(genuineReply.SafeDetail);
        await Assert.That(OrleansSerialization.Serialize(GrainReplyFactory.RequireValue(frames.Last().Final!.Value.Value!))
            .AsSpan().SequenceEqual(expected)).IsTrue();
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
