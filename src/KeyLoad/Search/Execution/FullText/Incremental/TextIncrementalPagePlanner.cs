using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Query;

namespace KeyLoad.Server.Search;

internal static class TextIncrementalPagePlanner
{
    private const ulong InitialRecord = 1;
    private const ulong RecordStep = 1;
    private const long SequenceStep = 1;
    private const long InitialRevisionBoundary = 0;
    private const string MapExceeded = "The text projection record map exceeds its budget.";

    internal static TextIncrementalPagePlan Plan(ProjectionBatch batch,
        TextIncrementalRecord[] records, ulong nextRecord, string collection, string field,
        ReadExecutionBudget budget, QueryExecutionOptions execution)
    {
        budget.Check();
        if (batch.Entries.IsDefault || batch.Consumer.Released || records is null
            || nextRecord < InitialRecord
            || batch.ThroughSequence < batch.Consumer.Checkpoint)
        {
            throw TextErrors.Corrupt();
        }
        var map = new Dictionary<EntityRef, TextIncrementalRecord>();
        var ids = new HashSet<ulong>();
        foreach (var record in records)
        {
            if (record.Id < InitialRecord || record.Id >= nextRecord || !ids.Add(record.Id)
                || record.Revision <= InitialRevisionBoundary || record.CanonicalSha256.Length != SHA256.HashSizeInBytes
                || record.Reference.Partition != batch.Consumer.Consumer.Partition
                || record.Reference.Collection != collection || !map.TryAdd(record.Reference, record))
            {
                throw TextErrors.Corrupt();
            }
        }
        var changes = new List<TextIncrementalChange>();
        var previousSequence = batch.Consumer.Checkpoint;
        foreach (var entry in batch.Entries)
        {
            budget.Check();
            if (previousSequence == long.MaxValue || entry.Sequence < previousSequence + SequenceStep
                || entry.Sequence > batch.ThroughSequence || entry.Commit.AtomicPartitionId != batch.Consumer.Consumer.Partition.AtomicPartitionId)
            {
                throw TextErrors.Corrupt();
            }
            var after = entry.After ?? throw TextErrors.Corrupt();
            map.TryGetValue(after.Reference, out var current);
            TextIncrementalRevision.Require(entry, current, batch.Consumer.Consumer.Partition, collection, budget);
            var id = current?.Id ?? Allocate(ref nextRecord);
            var removals = TextIncrementalTokens.Capture(entry.Before, field, id, budget, execution);
            var additions = TextIncrementalTokens.Capture(after, field, id, budget, execution);
            var retained = new TextIncrementalRecord(id, after.Reference, after.Revision,
                after.Deleted, TextIncrementalRevision.Digest(after, budget));
            changes.Add(new(current, retained, removals, additions));
            map[after.Reference] = retained;
            previousSequence = entry.Sequence;
        }
        budget.Check();
        return new(changes.ToArray(), map.Values.OrderBy(record => record.Id).ToArray(), nextRecord);
    }

    private static ulong Allocate(ref ulong nextRecord)
    {
        if (nextRecord == ulong.MaxValue)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, MapExceeded);
        }
        var result = nextRecord;
        nextRecord += RecordStep;
        return result;
    }
}
