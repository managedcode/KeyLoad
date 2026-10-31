using System.Runtime.CompilerServices;
using KeyLoad.Clustering;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RequestCqrsTextProducer
{
    internal static async IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> Run(
        Guid id, bool payload, RequestCqrsTextFault? fault, RequestCqrsProducerObservation observed,
        [EnumeratorCancellation] CancellationToken token)
    {
        var original = CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
            writer => ProduceAsync(writer, id, payload, fault, observed), token);
        await foreach (var chunk in original.WithCancellation(token))
        {
            yield return chunk;
        }
        if (fault == RequestCqrsTextFault.AfterFinal)
        {
            const int AfterFinalSequence = 9;
            yield return new(CqrsStreamChunkKind.Progress,
                Result<GrainRequestProgress>.Succeed(new GrainRequestProgress(id) { TextPhase = TextIndexMaintenancePhase.Completed }),
                null, TextMaintenanceProgress.PhaseCompleted, null, null, AfterFinalSequence);
        }
    }

    private static async ValueTask<Result<GrainOperationReply>> ProduceAsync(
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, Guid id, bool payload,
        RequestCqrsTextFault? fault, RequestCqrsProducerObservation observed)
    {
        try
        {
            await writer.StartedAsync(new(id));
            observed.MarkStarted();
            await FirstAsync(writer, id, fault);
            await TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.Capture);
            await TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.RuntimeIndex);
            await TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.Publish);
            await TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.Checkpoint);
            await TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.Completed);
            if (fault == RequestCqrsTextFault.ExtraCompleted)
            {
                await TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.Completed);
            }
            return Result<GrainOperationReply>.Succeed(new GrainOperationReply { Result = new GrainValue(payload) });
        }
        finally { observed.MarkProducerSettled(writer.CancellationToken.IsCancellationRequested); }
    }

    private static ValueTask FirstAsync(ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer,
        Guid id, RequestCqrsTextFault? fault)
        => fault switch
        {
            RequestCqrsTextFault.WrongIdentity => TextMaintenanceProgress.WriteAsync(writer, Guid.NewGuid(), TextIndexMaintenancePhase.Configure),
            RequestCqrsTextFault.UnexpectedPhase => TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.Intent),
            RequestCqrsTextFault.MixedPhase => writer.ProgressAsync(new(id)
            {
                TextPhase = TextIndexMaintenancePhase.Configure,
                AnnPhase = AnnMaintenancePhase.Completed
            }, TextMaintenanceProgress.PhaseCompleted),
            _ => TextMaintenanceProgress.WriteAsync(writer, id, TextIndexMaintenancePhase.Configure)
        };
}
