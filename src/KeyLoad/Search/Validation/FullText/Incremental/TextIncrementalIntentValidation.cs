using KeyLoad.Core;
using KeyLoad.Query.Search;

namespace KeyLoad.Server.Search;

internal static class TextIncrementalIntentValidation
{
    internal static void Require(TextIncrementalIntent intent, TextProjectionScope scope,
        ProjectionConsumerRef consumer, long generation, PhysicalShardRecord placement,
        int maximumRecords, int maximumChanges, ReadExecutionBudget budget)
    {
        budget.Check();
        if (intent is null || intent.FormatVersion != TextIncrementalProtocol.FormatVersion
            || intent.MaintenanceCommandId == Guid.Empty || intent.Scope != scope
            || intent.Consumer != consumer || intent.Generation != generation
            || intent.Placement is null || !SamePlacement(intent.Placement, placement)
            || consumer.Partition != scope.Partition
            || generation < TextIncrementalProtocol.InitialGeneration
            || intent.PreviousSequence < TextIncrementalProtocol.InitialSequence
            || intent.ThroughSequence < intent.PreviousSequence
            || intent.SourceUpperSequence < intent.ThroughSequence
            || intent.AppliedPosition < TextIncrementalProtocol.InitialSequence
            || !TextIncrementalDigest.IsCanonical(intent.ResourceSha256)
            || intent.CheckpointCommand is null
            || intent.CheckpointCommand.CommandId == Guid.Empty
            || intent.CheckpointCommand.Consumer != consumer
            || string.IsNullOrEmpty(intent.CheckpointCommand.Token)
            || intent.CheckpointCommand.Effects.IsDefault || !intent.CheckpointCommand.Effects.IsEmpty
            || intent.Changes is null || intent.Changes.Length > maximumChanges)
        {
            throw TextErrors.Corrupt();
        }
        TextIncrementalValidation.Records(intent.Records, intent.NextRecord, scope,
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
            {
                throw TextErrors.Corrupt();
            }
            RequirePostings(change.Removals, change.After.Id, budget);
            RequirePostings(change.Additions, change.After.Id, budget);
        }
        budget.Check();
    }

    private static bool SamePlacement(PhysicalShardRecord left, PhysicalShardRecord right)
        => left.PhysicalShardId == right.PhysicalShardId && left.Incarnation == right.Incarnation;

    private static void RequirePostings(TextIncrementalPosting[] postings, ulong record,
        ReadExecutionBudget budget)
    {
        if (postings is null)
        {
            throw TextErrors.Corrupt();
        }
        foreach (var posting in postings)
        {
            budget.ChargeBytes(TextProtocol.PostingBytes);
            if (posting is null || posting.Record != record)
            {
                throw TextErrors.Corrupt();
            }
        }
        budget.Check();
    }
}
