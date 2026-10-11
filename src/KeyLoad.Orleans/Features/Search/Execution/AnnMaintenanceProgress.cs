using ManagedCode.Communication.CQRS;

namespace KeyLoad.Orleans;

/// <summary>Owns the existing bounded public phase message for native ANN maintenance progress.</summary>
internal static class AnnMaintenanceProgress
{
    internal const string PhaseCompleted = "The native ANN maintenance phase completed.";
    internal static ValueTask WriteAsync(ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer,
        Guid parentRequestId, AnnMaintenancePhase phase)
        => writer.ProgressAsync(new GrainRequestProgress(parentRequestId) { AnnPhase = phase }, PhaseCompleted);
}
