using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

internal static class OnlineTextProgress
{
    internal const string PhaseCompleted = "The online text maintenance phase completed.";
    internal static async ValueTask WriteAsync(ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer,
        OnlineTextFrameAccounting frames, Guid parentRequestId, OnlineTextPhase phase)
    {
        frames.AdmitProgress();
        await writer.ProgressAsync(new GrainRequestProgress(parentRequestId) { OnlineTextPhase = phase }, PhaseCompleted)
            .ConfigureAwait(true);
    }
}
