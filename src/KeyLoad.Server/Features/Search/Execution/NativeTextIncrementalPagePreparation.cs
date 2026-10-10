using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalPagePreparation
{
    internal static NativeTextIncrementalManifest Prepare(NativeTextIncrementalSession session,
        ProjectionBatch page, CommitProjectionBatchRequest command, int maximumRecords,
        ReadExecutionBudget budget, QueryExecutionOptions query, IOptions<NativeTextExecutionOptions> options)
    {
        budget.Check();
        if (session.Intent is not null || page.Consumer.Consumer != session.Request.Consumer
            || page.Consumer.Definition.IndexGeneration != session.Request.IndexGeneration
            || page.Consumer.Released || page.Consumer.Checkpoint != session.Checkpoint
            || page.Entries.IsDefault || page.ThroughSequence < session.Checkpoint
            || page.ThroughSequence > session.CurrentReplayUpperSequence
            || command.CommandId == Guid.Empty || command.Consumer != session.Request.Consumer
            || command.Token != page.Token || command.Effects.IsDefault || !command.Effects.IsEmpty)
        { throw NativeTextErrors.Corrupt(); }
        var bootstrap = session.Manifest is null || session.Manifest.Bootstrap;
        var plan = Plan(session, page, bootstrap, maximumRecords, budget, query);
        var through = bootstrap ? session.CurrentReplayUpperSequence : page.ThroughSequence;
        var target = new NativeTextIncrementalManifest(NativeTextIncrementalProtocol.ManifestFormatVersion,
            session.Scope, session.Request.Consumer, session.Request.IndexGeneration, session.Request.Placement,
            through, session.Upper.AppliedPosition, plan.NextRecord, plan.Records,
            session.Manifest?.Files ?? [], TextProjectionProtocol.TokenizerVersion,
            TextProjectionProtocol.HashVersion, session.Upper.ResourceSha256, bootstrap,
            session.Manifest?.LastSettledCheckpointRequest);
        var intent = new NativeTextIncrementalIntent(NativeTextIncrementalProtocol.FormatVersion,
            session.Request.CommandId, session.Scope, session.Request.Consumer, session.Request.IndexGeneration,
            session.Request.Placement, page.Consumer.Checkpoint, page.ThroughSequence, command,
            plan.Changes, plan.Records, plan.NextRecord, bootstrap,
            session.Upper.ResourceSha256, session.CurrentReplayUpperSequence, session.Upper.AppliedPosition);
        NativeTextIncrementalMetadata.PersistIntent(RequireOwner(session).Path, intent,
            options.Value.MaximumDiskBytes, budget, options, RequireOwner(session).Resources);
        session.Intent = intent;
        RequireOwner(session).Observe(NativeTextFaultStage.IncrementalIntentFlushed);
        return target;
    }

    private static NativeTextIncrementalPagePlan Plan(NativeTextIncrementalSession session,
        ProjectionBatch page, bool bootstrap, int maximumRecords, ReadExecutionBudget budget,
        QueryExecutionOptions query)
    {
        if (session.Manifest is null)
        {
            var seed = NativeTextIncrementalSeedPlanner.Plan(session.Upper, session.Scope,
                maximumRecords, budget, query);
            var changes = new NativeTextIncrementalChange[seed.Records.Length];
            for (var index = NativeTextIncrementalSourceProtocol.EmptyRecords; index < changes.Length; index++)
            {
                budget.Check();
                changes[index] = new(null, seed.Records[index], [], seed.Postings[index]);
            }
            return new(changes, seed.Records, seed.NextRecord);
        }
        if (bootstrap)
        { return new([], session.Manifest.Records, session.Manifest.NextRecord); }
        return NativeTextIncrementalPagePlanner.Plan(page, session.Manifest.Records,
            session.Manifest.NextRecord, session.Request.Collection, session.Request.Field,
            maximumRecords, budget, query);
    }

    internal static NativeTextIncrementalNativeOwner RequireOwner(NativeTextIncrementalSession session)
        => session.NativeOwner ?? throw NativeTextErrors.Ownership();
}
