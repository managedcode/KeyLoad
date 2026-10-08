using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalIntentValidation
{
    internal static void Require(NativeTextIncrementalIntent intent, TextProjectionScope scope,
        ProjectionConsumerRef consumer, long generation, PhysicalShardRecord placement,
        int maximumRecords, int maximumChanges, ReadExecutionBudget budget)
    {
        budget.Check();
        if (intent is null || intent.FormatVersion != NativeTextIncrementalProtocol.FormatVersion
            || intent.MaintenanceCommandId == Guid.Empty || intent.Scope != scope
            || intent.Consumer != consumer || intent.Generation != generation
            || intent.Placement is null || !SamePlacement(intent.Placement, placement)
            || consumer.Partition != scope.Partition
            || generation < NativeTextIncrementalProtocol.InitialGeneration
            || intent.PreviousSequence < NativeTextIncrementalProtocol.InitialSequence
            || intent.ThroughSequence < intent.PreviousSequence
            || intent.SourceUpperSequence < intent.ThroughSequence
            || intent.AppliedPosition < NativeTextIncrementalProtocol.InitialSequence
            || !NativeTextIncrementalDigest.IsCanonical(intent.ResourceSha256)
            || intent.CheckpointCommand is null
            || intent.CheckpointCommand.CommandId == Guid.Empty
            || intent.CheckpointCommand.Consumer != consumer
            || string.IsNullOrEmpty(intent.CheckpointCommand.Token)
            || intent.CheckpointCommand.Effects.IsDefault || !intent.CheckpointCommand.Effects.IsEmpty
            || intent.Changes is null || intent.Changes.Length > maximumChanges)
        { throw NativeTextErrors.Corrupt(); }
        NativeTextIncrementalValidation.Records(intent.Records, intent.NextRecord, scope,
            maximumRecords, budget);
        var retained = intent.Records.ToDictionary(record => record.Id);
        foreach (var change in intent.Changes)
        {
            budget.Check();
            if (change is null || change.After is null
                || !retained.TryGetValue(change.After.Id, out var final)
                || final.Reference != change.After.Reference
                || change.After.Reference.Partition != scope.Partition
                || change.After.Reference.Collection != scope.Collection
                || change.Before is not null && (change.Before.Id != change.After.Id
                    || change.Before.Reference != change.After.Reference
                    || change.Before.Revision >= change.After.Revision))
            { throw NativeTextErrors.Corrupt(); }
            RequirePostings(change.Removals, change.After.Id, budget);
            RequirePostings(change.Additions, change.After.Id, budget);
        }
        budget.Check();
    }

    private static bool SamePlacement(PhysicalShardRecord left, PhysicalShardRecord right)
        => left.PhysicalShardId == right.PhysicalShardId && left.Incarnation == right.Incarnation
            && left.PlacementEpoch == right.PlacementEpoch
            && left.VoterIds.SequenceEqual(right.VoterIds, StringComparer.Ordinal);

    private static void RequirePostings(NativeTextIncrementalPosting[] postings, ulong record,
        ReadExecutionBudget budget)
    {
        if (postings is null)
        { throw NativeTextErrors.Corrupt(); }
        foreach (var posting in postings)
        {
            budget.ChargeBytes(NativeTextProtocol.PostingBytes);
            if (posting is null || posting.Record != record)
            { throw NativeTextErrors.Corrupt(); }
        }
        budget.Check();
    }
}
