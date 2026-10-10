using System.Runtime.CompilerServices;
using KeyLoad.Orleans;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsTextProducer
{
    internal static async IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> Run(
        Guid id, byte[] payload, RequestCqrsTextFault? fault, RequestCqrsProducerObservation observed, int maximumFrames,
        [EnumeratorCancellation] CancellationToken token)
    {
        var original = CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
            writer => ProduceAsync(writer, id, payload, fault, observed, maximumFrames), token);
        await foreach (var chunk in original.WithCancellation(token))
        { yield return chunk; }
        if (fault == RequestCqrsTextFault.AfterFinal)
        {
            const int AfterFinalSequence = 9;
            yield return new(CqrsStreamChunkKind.Progress,
                Result<GrainRequestProgress>.Succeed(new GrainRequestProgress(id) { TextPhase = TextIndexMaintenancePhase.Completed }),
                null, TextMaintenanceProgress.PhaseCompleted, null, null, AfterFinalSequence);
        }
    }

    private static async ValueTask<Result<GrainOperationReply>> ProduceAsync(
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, Guid id, byte[] payload,
        RequestCqrsTextFault? fault, RequestCqrsProducerObservation observed, int maximumFrames)
    {
        try
        {
            await writer.StartedAsync(new(id));
            observed.MarkStarted();
            await FirstAsync(writer, id, fault);
            await TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.Capture);
            await TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.NativeIndex);
            await TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.Publish);
            await TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.Checkpoint);
            if (fault == RequestCqrsTextFault.FrameBudget)
            { await ExhaustFramesAsync(writer, id, maximumFrames); }
            await TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.Completed);
            if (fault == RequestCqrsTextFault.ExtraCompleted)
            { await TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.Completed); }
            return Result<GrainOperationReply>.Succeed(new GrainOperationReply { Payload = payload });
        }
        finally { observed.MarkProducerSettled(writer.CancellationToken.IsCancellationRequested); }
    }

    private static async ValueTask ExhaustFramesAsync(ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer,
        Guid id, int maximumFrames)
    {
        const int FramesPerPage = 3;
        for (var page = 0; page <= maximumFrames / FramesPerPage; page++)
        {
            await TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.NativeIndex);
            await TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.Publish);
            await TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.Checkpoint);
        }
    }

    private static ValueTask FirstAsync(ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer,
        Guid id, RequestCqrsTextFault? fault)
        => fault switch
        {
            RequestCqrsTextFault.WrongIdentity => TextMaintenanceProgress.WriteAsync(writer, Guid.NewGuid(), TextIndexMaintenancePhase.Configure),
            RequestCqrsTextFault.UnexpectedPhase => TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.Intent),
            RequestCqrsTextFault.MixedPhase => writer.ProgressAsync(new(id)
            { TextPhase = TextIndexMaintenancePhase.Configure, AnnPhase = AnnMaintenancePhase.Completed }, TextMaintenanceProgress.PhaseCompleted),
            _ => TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.Configure)
        };
}
