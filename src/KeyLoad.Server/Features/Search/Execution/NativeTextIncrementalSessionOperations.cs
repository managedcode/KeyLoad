using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Orleans;
using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalSessionOperations
{
    internal static TextMaintenanceCapabilityResult Execute(DatabaseEngine database,
        NativeTextIncrementalSession session, PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, QueryExecutionOptions query,
        int maximumRecords, int maximumChanges, IOptions<NativeTextExecutionOptions> options,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var stage = session.Budget.EnterStageCancellation(token);
        session.Require(request.SessionId, request.Maintenance, principal.Id);
        var fresh = NativeTextSeedCollector.Capture(database, principal.Id,
            new(session.Request.Consumer, session.Request.IndexGeneration, session.Request.Collection,
                session.Request.Field, session.Request.NodeId, session.Request.Placement), session.Budget);
        session.RequireUpper(fresh);
        switch (request.Kind)
        {
            case TextMaintenanceCapabilityKind.PreparePage:
                session.Target = NativeTextIncrementalPagePreparation.Prepare(session,
                    request.Page ?? throw NativeTextErrors.Corrupt(),
                    request.CheckpointIntent ?? throw NativeTextErrors.Corrupt(), maximumRecords,
                    session.Budget, query, options);
                break;
            case TextMaintenanceCapabilityKind.ApplyIntent:
                session.Manifest = NativeTextIncrementalGenerationFlow.Apply(
                    NativeTextIncrementalPagePreparation.RequireOwner(session),
                    session.Intent ?? throw NativeTextErrors.Corrupt(),
                    session.Target ?? throw NativeTextErrors.Corrupt(), maximumRecords, maximumChanges,
                    session.Budget, options);
                break;
            case TextMaintenanceCapabilityKind.SettleCheckpoint:
                Settle(session, request.Acknowledged ?? throw NativeTextErrors.Corrupt(), fresh, options);
                break;
            case TextMaintenanceCapabilityKind.Verify:
                Verify(session, fresh, options);
                break;
            default:
                throw NativeTextErrors.Mismatch();
        }
        session.Budget.Check();
        return NativeTextIncrementalCapabilityResult.Create(session, fresh);
    }

    private static void Verify(NativeTextIncrementalSession session, NativeTextSeedCapture fresh,
        IOptions<NativeTextExecutionOptions> options)
    {
        var manifest = session.Manifest ?? throw NativeTextErrors.Corrupt();
        if (session.Intent is not null || manifest.Bootstrap
            || manifest.ThroughSequence != session.ReplayUpperSequence
            || session.Checkpoint != session.ReplayUpperSequence)
        { throw NativeTextErrors.Mismatch(); }
        if (manifest.ThroughSequence == fresh.UpperSequence)
        { NativeTextIncrementalSourceValidation.CompleteCorpus(manifest, fresh, session.Budget); }
        NativeTextIncrementalPublishedInventory.Require(
            NativeTextIncrementalPagePreparation.RequireOwner(session), manifest, session.Budget, options);
        session.Budget.Check();
    }

    private static void Settle(NativeTextIncrementalSession session, ProjectionBatchResult acknowledged,
        NativeTextSeedCapture fresh, IOptions<NativeTextExecutionOptions> options)
    {
        var original = session.Intent ?? throw NativeTextErrors.Corrupt();
        session.Manifest = NativeTextIncrementalCheckpointSettlement.Complete(
            NativeTextIncrementalPagePreparation.RequireOwner(session).Path, original,
            session.Manifest ?? throw NativeTextErrors.Corrupt(), acknowledged, fresh, session.Budget, options,
            NativeTextIncrementalPagePreparation.RequireOwner(session).FaultObserver);
        session.Checkpoint = original.ThroughSequence;
        session.Intent = null;
        session.Target = null;
    }
}
