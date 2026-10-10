using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlineCheckpointSettlement
{
    internal static async Task ExecuteAsync(DatabaseEngine database, NativeTextOnlineSession session,
        ProjectionBatchResult acknowledged, NativeTextResourceOwnership resources, ServerRuntimeOptions options)
    {
        var original = await session.RequireOriginalCheckpointAsync(acknowledged).ConfigureAwait(false);
        lock (session.Gate)
        {
            var budget = session.Budget;
            var intent = session.Intent ?? throw NativeTextErrors.Ownership();
            var fresh = NativeTextOnlineValidation.CaptureFull(database, session);
            NativeTextOnlineCapture.RequireSameAuthority(session, fresh);
            session.Manifest = NativeTextIncrementalCheckpointSettlement.Complete(
                NativeTextOnlinePagePreparation.RequireOwner(session).Path, intent,
                session.Manifest ?? throw NativeTextErrors.Ownership(), original, fresh, budget,
                options.NativeText, resources: resources);
            session.Manifest = session.Manifest with { AppliedPosition = fresh.AppliedPosition };
            NativeTextIncrementalMetadata.Publish(NativeTextOnlinePagePreparation.RequireOwner(session).Path,
                session.Manifest, options.NativeText.Value.MaximumDiskBytes, budget, options.NativeText, resources);
            session.Current = fresh;
            session.Checkpoint = original;
            session.Intent = null;
            session.Target = null;
            session.CompleteCheckpointAfterIntentRetired();
        }
    }
}
