using System.Runtime.CompilerServices;
using KeyLoad.Clustering;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsAnnProducer
{
    private const int FirstOffset = 0;
    private const int OneStep = 1;
    private const string WrongMessage = "Unexpected ANN phase message.";
    private const string WrongEvent = "unexpected-ann-event";
    private const string MissingSequence = "The controlled native stream did not supply a sequence.";

    internal static async IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> Run(
        Guid id, GrainOperationReply reply, RequestCqrsAnnFault? fault, RequestCqrsProducerObservation observed,
        int maximumFrames, [EnumeratorCancellation] CancellationToken token, int replayCount = FirstOffset)
    {
        long last = FirstOffset;
        var original = CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
            writer => ProduceAsync(writer, id, reply, fault, observed, maximumFrames, replayCount), token);
        await foreach (var chunk in original.WithCancellation(token))
        {
            last = chunk.Sequence ?? throw new InvalidOperationException(MissingSequence);
            yield return chunk.Kind == CqrsStreamChunkKind.Progress ? Alter(chunk, fault) : chunk;
        }
        if (fault == RequestCqrsAnnFault.AfterFinal)
        {
            yield return new(CqrsStreamChunkKind.Progress,
            Result<GrainRequestProgress>.Succeed(new GrainRequestProgress(id) { AnnPhase = AnnMaintenancePhase.Completed }),
            final: null, message: AnnMaintenanceProgress.PhaseCompleted,
            eventType: CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(CqrsStreamChunkKind.Progress),
            eventId: null, sequence: last + OneStep);
        }
    }

    private static async ValueTask<Result<GrainOperationReply>> ProduceAsync(
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, Guid id, GrainOperationReply reply,
        RequestCqrsAnnFault? fault, RequestCqrsProducerObservation observed, int maximumFrames, int replayCount)
    {
        try
        {
            await writer.StartedAsync(new(id));
            observed.MarkStarted();
            await FirstAsync(writer, id, reply, fault);
            await AnnMaintenanceProgress.WriteAsync(writer, id, AnnMaintenancePhase.Capture);
            if (fault == RequestCqrsAnnFault.FrameBudget)
            {
                for (var index = FirstOffset; index <= maximumFrames; index++)
                {
                    await AnnMaintenanceProgress.WriteAsync(writer, id, AnnMaintenancePhase.Replay);
                }
            }
            for (var index = FirstOffset; index < replayCount; index++)
            {
                await AnnMaintenanceProgress.WriteAsync(writer, id, AnnMaintenancePhase.Replay);
            }
            await AnnMaintenanceProgress.WriteAsync(writer, id, AnnMaintenancePhase.Verify);
            await AnnMaintenanceProgress.WriteAsync(writer, id, AnnMaintenancePhase.NativeIndex);
            await AnnMaintenanceProgress.WriteAsync(writer, id, AnnMaintenancePhase.Publish);
            await AnnMaintenanceProgress.WriteAsync(writer, id, AnnMaintenancePhase.Checkpoint);
            if (fault != RequestCqrsAnnFault.MissingCompleted)
            {
                await AnnMaintenanceProgress.WriteAsync(writer, id, AnnMaintenancePhase.Completed);
            }
            if (fault == RequestCqrsAnnFault.ExtraCompleted)
            {
                await AnnMaintenanceProgress.WriteAsync(writer, id, AnnMaintenancePhase.Completed);
            }
            return Result<GrainOperationReply>.Succeed(reply);
        }
        finally { observed.MarkProducerSettled(writer.CancellationToken.IsCancellationRequested); }
    }

    private static ValueTask FirstAsync(ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer,
        Guid id, GrainOperationReply reply, RequestCqrsAnnFault? fault)
    {
        var checkpoint = ((AnnMaintenanceResult)NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value!).Checkpoint!;
        var progress = new GrainRequestProgress(fault == RequestCqrsAnnFault.WrongIdentity ? Guid.NewGuid() : id)
        {
            AnnPhase = fault == RequestCqrsAnnFault.UnexpectedPhase ? AnnMaintenancePhase.Release : AnnMaintenancePhase.Configure,
            TextPhase = fault == RequestCqrsAnnFault.MixedText ? TextIndexMaintenancePhase.Configure : null,
            MovePhase = fault == RequestCqrsAnnFault.MixedMove ? PartitionMovePhase.Prepared : null,
            OnlineTextPhase = fault == RequestCqrsAnnFault.MixedOnline ? OnlineTextPhase.Captured : null,
            EventFeedTail = fault == RequestCqrsAnnFault.MixedTail
                ? new(RequestCqrsAnnTestData.Version, new()
                {
                    Version = RequestCqrsAnnTestData.Version,
                    Receipt = checkpoint.Receipt
                }) : null
        };
        return writer.ProgressAsync(progress, AnnMaintenanceProgress.PhaseCompleted);
    }

    private static CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> Alter(
        CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk, RequestCqrsAnnFault? fault)
        => new(chunk.Kind, progressResult: chunk.ProgressResult, final: chunk.Final,
            message: fault == RequestCqrsAnnFault.WrongMessage ? WrongMessage : chunk.Message,
            eventType: fault == RequestCqrsAnnFault.WrongEvent ? WrongEvent : chunk.EventType,
            eventId: chunk.EventId,
            sequence: fault == RequestCqrsAnnFault.WrongSequence ? chunk.Sequence + OneStep : chunk.Sequence);
}
