using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlinePagePreparation
{
    internal static void Execute(DatabaseEngine database, NativeTextOnlineSession session,
        ProjectionBatch page, CommitProjectionBatchRequest command, NativeTextResourceOwnership resources,
        ServerRuntimeOptions options)
    {
        var budget = session.Budget;
        var manifest = session.Manifest ?? throw NativeTextErrors.Ownership();
        var original = session.Base ?? throw NativeTextErrors.Ownership();
        var fresh = NativeTextOnlineCapture.CurrentMetadata(database, session);
        NativeTextOnlineCapture.RequireSameAuthority(session, fresh);
        var checkpoint = session.Checkpoint?.Checkpoint ?? original.Checkpoint;
        if (session.Intent is not null || session.CheckpointWork is not null
            || page.Consumer.Consumer != session.Request.Consumer || page.Consumer.Released
            || page.Consumer.Definition.IndexGeneration != session.Request.ConsumerGeneration
            || page.Consumer.Checkpoint != checkpoint || page.Entries.IsDefault
            || page.ThroughSequence < checkpoint || page.ThroughSequence > fresh.UpperSequence
            || command.CommandId == Guid.Empty || command.Consumer != session.Request.Consumer
            || command.Token != page.Token || command.Effects.IsDefault || !command.Effects.IsEmpty)
        { throw NativeTextErrors.Corrupt(); }
        var filtered = FilterCoveredSeed(page, manifest.ThroughSequence, budget);
        var plan = NativeTextIncrementalPagePlanner.Plan(filtered, manifest.Records, manifest.NextRecord,
            session.Request.Collection, session.Request.Field, database.Limits.MaxScanRecords,
            budget, options.Core.QueryExecution.Value);
        var through = Math.Max(manifest.ThroughSequence, page.ThroughSequence);
        var bootstrap = page.ThroughSequence < original.UpperSequence;
        var target = manifest with
        {
            ThroughSequence = through,
            AppliedPosition = fresh.AppliedPosition,
            Records = plan.Records,
            NextRecord = plan.NextRecord,
            Bootstrap = bootstrap
        };
        var intent = new NativeTextIncrementalIntent(NativeTextIncrementalProtocol.FormatVersion,
            session.Request.CommandId, manifest.Scope, session.Request.Consumer, session.Request.ConsumerGeneration,
            session.Request.Placement, checkpoint, page.ThroughSequence, command, plan.Changes, plan.Records,
            plan.NextRecord, bootstrap, original.ResourceSha256, fresh.UpperSequence, fresh.AppliedPosition);
        NativeTextIncrementalIntentValidation.Require(intent, manifest.Scope, session.Request.Consumer,
            session.Request.ConsumerGeneration, session.Request.Placement, database.Limits.MaxScanRecords,
            database.Limits.MaxScanRecords, budget);
        NativeTextIncrementalMetadata.PersistIntent(RequireOwner(session).Path, intent,
            options.NativeText.Value.MaximumDiskBytes, budget, options.NativeText, resources);
        session.Intent = intent;
        session.Target = target;
        session.CheckpointIntent = command;
        session.Current = fresh;
        session.RegisterCheckpoint(intent);
    }

    private static ProjectionBatch FilterCoveredSeed(ProjectionBatch actual, long covered,
        ReadExecutionBudget budget)
    {
        var entries = ImmutableArray.CreateBuilder<OutboxEntry>();
        foreach (var entry in actual.Entries)
        {
            budget.Check();
            if (entry.Sequence <= covered)
            { continue; }
            budget.ChargeBytes(NativeTextIncrementalSourceProtocol.MapSlotBytes);
            entries.Add(entry);
        }
        return actual with
        {
            Consumer = actual.Consumer with { Checkpoint = Math.Max(actual.Consumer.Checkpoint, covered) },
            Entries = entries.ToImmutable(),
            ThroughSequence = Math.Max(actual.ThroughSequence, covered)
        };
    }

    internal static NativeTextIncrementalNativeOwner RequireOwner(NativeTextOnlineSession session)
        => session.NativeOwner ?? throw NativeTextErrors.Ownership();
}
