using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Search;
using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextIncrementalSessionOperations
{
    internal static TextMaintenanceCapabilityResult Execute(DatabaseEngine database,
        TextIncrementalSession session, PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, QueryExecutionOptions query,
        int maximumRecords, int maximumChanges, IOptions<TextExecutionOptions> options,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        session.Require(request.SessionId, request.Maintenance, principal.Id);
        var fresh = TextSeedCollector.Capture(database, principal.Id,
            new(session.Request.Consumer, session.Request.IndexGeneration, session.Request.Collection,
                session.Request.Field, session.Request.NodeId, session.Request.Placement), session.Budget);
        session.RequireUpper(fresh);
        switch (request.Kind)
        {
            case TextMaintenanceCapabilityKind.PreparePage:
                session.Target = TextIncrementalPagePreparation.Prepare(session,
                    request.Page ?? throw TextErrors.Corrupt(),
                    request.CheckpointIntent ?? throw TextErrors.Corrupt(), maximumRecords,
                    session.Budget, query, options);
                break;
            case TextMaintenanceCapabilityKind.ApplyIntent:
                session.Manifest = TextIncrementalGenerationFlow.Apply(
                    TextIncrementalPagePreparation.RequireOwner(session),
                    session.Intent ?? throw TextErrors.Corrupt(),
                    session.Target ?? throw TextErrors.Corrupt(), maximumRecords, maximumChanges,
                    session.Budget, options);
                break;
            case TextMaintenanceCapabilityKind.SettleCheckpoint:
                Settle(session, request.Acknowledged ?? throw TextErrors.Corrupt(), fresh, options);
                break;
            case TextMaintenanceCapabilityKind.Verify:
                Verify(session, fresh, options);
                break;
            default:
                throw TextErrors.Mismatch();
        }
        session.Budget.Check();
        return TextIncrementalCapabilityResult.Create(session, fresh);
    }

    private static void Verify(TextIncrementalSession session, TextSeedCapture fresh,
        IOptions<TextExecutionOptions> options)
    {
        var manifest = session.Manifest ?? throw TextErrors.Corrupt();
        if (session.Intent is not null || manifest.Bootstrap
            || manifest.ThroughSequence != session.ReplayUpperSequence
            || session.Checkpoint != session.ReplayUpperSequence)
        {
            throw TextErrors.Mismatch();
        }
        if (manifest.ThroughSequence == fresh.UpperSequence)
        {
            TextIncrementalSourceValidation.CompleteCorpus(manifest, fresh, session.Budget);
        }
        TextIncrementalPublishedInventory.Require(
            TextIncrementalPagePreparation.RequireOwner(session), manifest, session.Budget, options);
        session.Budget.Check();
    }

    private static void Settle(TextIncrementalSession session, ProjectionBatchResult acknowledged,
        TextSeedCapture fresh, IOptions<TextExecutionOptions> options)
    {
        var original = session.Intent ?? throw TextErrors.Corrupt();
        session.Manifest = TextIncrementalCheckpointSettlement.Complete(
            TextIncrementalPagePreparation.RequireOwner(session).Path, original,
            session.Manifest ?? throw TextErrors.Corrupt(), acknowledged, fresh, session.Budget, options,
            TextIncrementalPagePreparation.RequireOwner(session).FaultObserver,
            TextIncrementalPagePreparation.RequireOwner(session).Resources);
        session.Checkpoint = original.ThroughSequence;
        session.Intent = null;
        session.Target = null;
    }
}
