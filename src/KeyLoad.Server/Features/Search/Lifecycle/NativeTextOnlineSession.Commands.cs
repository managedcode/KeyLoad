using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextOnlineSession
{
    internal void RegisterCheckpoint(NativeTextIncrementalIntent original)
    {
        Budget.Check();
        if (CheckpointWork is not null || Intent != original)
        { throw NativeTextErrors.Mismatch(); }
        CheckpointWork = new(Id, original.CheckpointCommand, PrincipalId, OriginalExpiry,
            original.ThroughSequence, Budget);
    }

    internal OnlineTextPublicationWork? TryMatchCheckpoint(CommitProjectionBatchRequest command,
        string actualPrincipal, DateTimeOffset expiry)
        => CheckpointWork?.TryMatch(command, actualPrincipal, expiry, Budget);

    internal async Task<ProjectionBatchResult> RequireOriginalCheckpointAsync(ProjectionBatchResult acknowledged)
        => await (CheckpointWork ?? throw NativeTextErrors.Ownership())
            .RequireAcknowledgedAsync(acknowledged, Budget).ConfigureAwait(false);

    internal void CompleteCheckpointAfterIntentRetired()
    {
        Budget.Check();
        if (Intent is not null || Target is not null || CheckpointWork is null)
        { throw NativeTextErrors.Mismatch(); }
        _ = CheckpointWork.Work.CloseAdmissionAndCaptureOriginal();
        CheckpointWork = null;
    }
}
