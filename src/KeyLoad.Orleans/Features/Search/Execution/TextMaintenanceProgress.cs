using ManagedCode.Communication.CQRS;

namespace KeyLoad.Orleans;

internal static class TextMaintenanceProgress
{
    private const string PhaseCompleted = "The native text maintenance phase completed.";

    internal static ValueTask WriteAsync(ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer,
        Guid parentRequestId, TextIndexMaintenancePhase phase)
        => writer.ProgressAsync(new GrainRequestProgress(parentRequestId) { TextPhase = phase }, PhaseCompleted);
}
