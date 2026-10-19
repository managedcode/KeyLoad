using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Owns the existing bounded public phase message for execution ANN maintenance progress.</summary>
internal static class AnnMaintenanceProgress
{
    internal const string PhaseCompleted = "The execution ANN maintenance phase completed.";
    internal static ValueTask WriteAsync(ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer,
        Guid parentRequestId, AnnMaintenancePhase phase)
        => writer.ProgressAsync(new GrainRequestProgress(parentRequestId) { AnnPhase = phase }, PhaseCompleted);
}
