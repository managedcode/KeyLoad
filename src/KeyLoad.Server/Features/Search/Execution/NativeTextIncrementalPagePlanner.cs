using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Query;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalPagePlanner
{
    private const ulong InitialRecord = 1;
    private const ulong RecordStep = 1;
    private const long SequenceStep = 1;
    private const long InitialRevisionBoundary = 0;
    private const string MapExceeded = "The text projection record map exceeds its budget.";

    internal static NativeTextIncrementalPagePlan Plan(ProjectionBatch batch,
        NativeTextIncrementalRecord[] records, ulong nextRecord, string collection, string field,
        int maximumRecords, ReadExecutionBudget budget, QueryExecutionOptions execution)
    {
        budget.Check();
        if (batch.Entries.IsDefault || batch.Consumer.Released || records is null
            || nextRecord < InitialRecord || records.Length > maximumRecords
            || batch.ThroughSequence < batch.Consumer.Checkpoint)
        { throw NativeTextErrors.Corrupt(); }
        budget.ChargeBytes(checked((long)records.Length * NativeTextIncrementalSourceProtocol.MapSlotBytes));
        var map = new Dictionary<EntityRef, NativeTextIncrementalRecord>();
        var ids = new HashSet<ulong>();
        foreach (var record in records)
        {
            budget.ChargeBytes(NativeSerialization.Measure(record));
            budget.ChargeBytes(sizeof(ulong));
            if (record.Id < InitialRecord || record.Id >= nextRecord || !ids.Add(record.Id)
                || record.Revision <= InitialRevisionBoundary || record.CanonicalSha256.Length != SHA256.HashSizeInBytes
                || record.Reference.Partition != batch.Consumer.Consumer.Partition
                || record.Reference.Collection != collection || !map.TryAdd(record.Reference, record))
            { throw NativeTextErrors.Corrupt(); }
        }
        var changes = new List<NativeTextIncrementalChange>();
        var previousSequence = batch.Consumer.Checkpoint;
        foreach (var entry in batch.Entries)
        {
            budget.Check();
            if (previousSequence == long.MaxValue || entry.Sequence < previousSequence + SequenceStep
                || entry.Sequence > batch.ThroughSequence || entry.Commit.AtomicPartitionId != batch.Consumer.Consumer.Partition.AtomicPartitionId)
            { throw NativeTextErrors.Corrupt(); }
            var after = entry.After ?? throw NativeTextErrors.Corrupt();
            map.TryGetValue(after.Reference, out var current);
            NativeTextIncrementalRevision.Require(entry, current, batch.Consumer.Consumer.Partition, collection, budget);
            var id = current?.Id ?? Allocate(map.Count, maximumRecords, ref nextRecord);
            var removals = NativeTextIncrementalTokens.Capture(entry.Before, field, id, budget, execution);
            var additions = NativeTextIncrementalTokens.Capture(after, field, id, budget, execution);
            var retained = new NativeTextIncrementalRecord(id, after.Reference, after.Revision,
                after.Deleted, NativeTextIncrementalRevision.Digest(after, budget));
            changes.Add(new(current, retained, removals, additions));
            map[after.Reference] = retained;
            previousSequence = entry.Sequence;
        }
        budget.Check();
        return new(changes.ToArray(), map.Values.OrderBy(record => record.Id).ToArray(), nextRecord);
    }

    private static ulong Allocate(int count, int maximumRecords, ref ulong nextRecord)
    {
        if (count >= maximumRecords || nextRecord == ulong.MaxValue)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, MapExceeded); }
        var result = nextRecord;
        nextRecord += RecordStep;
        return result;
    }
}
