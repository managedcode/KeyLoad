using KeyLoad.Core;

namespace KeyLoad.Server.Search;

internal static class TextOnlineCheckpointSettlement
{
    internal static async Task ExecuteAsync(DatabaseEngine database, TextOnlineSession session,
        ProjectionBatchResult acknowledged, TextResourceOwnership resources, ServerRuntimeOptions options)
    {
        var original = await session.RequireOriginalCheckpointAsync(acknowledged);
        lock (session.Gate)
        {
            var budget = session.Budget;
            var intent = session.Intent ?? throw TextErrors.Ownership();
            var fresh = TextOnlineValidation.CaptureFull(database, session);
            TextOnlineCapture.RequireSameAuthority(session, fresh);
            session.Manifest = TextIncrementalCheckpointSettlement.Complete(
                TextOnlinePagePreparation.RequireOwner(session).Path, intent,
                session.Manifest ?? throw TextErrors.Ownership(), original, fresh, budget,
                options.RuntimeText, resources: resources);
            session.Manifest = session.Manifest with
            {
                AppliedPosition = fresh.AppliedPosition
            };
            TextIncrementalMetadata.Publish(TextOnlinePagePreparation.RequireOwner(session).Path,
                session.Manifest, options.RuntimeText.Value.MaximumDiskBytes, budget, options.RuntimeText, resources);
            session.Current = fresh;
            session.Checkpoint = original;
            session.Intent = null;
            session.Target = null;
            session.CompleteCheckpointAfterIntentRetired();
        }
    }
}
