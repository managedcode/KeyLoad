using KeyLoad.Clustering;

namespace KeyLoad.Server.Search;

internal sealed partial class TextOnlineSession
{
    internal void RegisterCheckpoint(TextIncrementalIntent original)
    {
        Budget.Check();
        if (CheckpointWork is not null || Intent != original)
        {
            throw TextErrors.Mismatch();
        }
        CheckpointWork = new(Id, original.CheckpointCommand, PrincipalId, OriginalExpiry,
            original.ThroughSequence, Budget);
    }

    internal OnlineTextPublicationWork? TryMatchCheckpoint(CommitProjectionBatchRequest command,
        string actualPrincipal, DateTimeOffset expiry)
        => CheckpointWork?.TryMatch(command, actualPrincipal, expiry, Budget).Value;

    internal async Task<ProjectionBatchResult> RequireOriginalCheckpointAsync(ProjectionBatchResult acknowledged)
        => await (CheckpointWork ?? throw TextErrors.Ownership())
            .RequireAcknowledgedAsync(acknowledged, Budget);

    internal void CompleteCheckpointAfterIntentRetired()
    {
        Budget.Check();
        if (Intent is not null || Target is not null || CheckpointWork is null)
        {
            throw TextErrors.Mismatch();
        }
        _ = CheckpointWork.Work.CloseAdmissionAndCaptureOriginal();
        CheckpointWork = null;
    }
}
