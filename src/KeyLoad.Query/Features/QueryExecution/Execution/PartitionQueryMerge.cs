using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class PartitionQueryMerge
{
    private const int Version = 1;
    private const string InvalidResultMessage = "A partition query leaf result is invalid.";
    private const string InconsistentOwnerMessage = "Partition query leaves do not share one owner and policy epoch.";
    private const string DuplicateReferenceMessage = "Partition query leaves returned a duplicate entity reference.";
    private const string IncompleteMessage = "A partition query leaf did not complete.";

    internal static PartitionQueryResultV1 Complete(PartitionQueryPlanV1 plan,
        ImmutableArray<PartitionQueryLeafResultV1> leaves, StoreIdentity owner, DatabaseLimits limits,
        ReadExecutionBudget budget, QueryExecutionOptions execution)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(budget);
        var partitions = PartitionQueryPlanValidation.Validate(plan, limits, execution);
        if (leaves.IsDefault || leaves.Length != plan.Leaves.Length)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, IncompleteMessage);
        }

        var totals = ValidateLeaves(plan, leaves, partitions, owner, budget);
        var query = plan.Leaves[0].Request.Query;
        var rows = MergeRuns(leaves, query.Order, plan.Limit, budget);
        var retainedBytes = RetainedBytes(leaves, rows.Length, budget);
        if (retainedBytes > plan.MaxRetainedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, "The partition query merge exceeds its reserved budget.");
        }

        return new(Version, true, leaves, rows, totals.ExaminedRecords, totals.ReadBytes, retainedBytes);
    }

    private static (int ExaminedRecords, long ReadBytes, long RetainedBytes) ValidateLeaves(
        PartitionQueryPlanV1 plan, ImmutableArray<PartitionQueryLeafResultV1> leaves,
        PartitionRef[] partitions, StoreIdentity owner, ReadExecutionBudget budget)
    {
        var examined = 0;
        long readBytes = 0;
        long retained = 0;
        long? policyEpoch = null;
        for (var index = 0; index < leaves.Length; index++)
        {
            budget.Check();
            var leaf = leaves[index];
            ValidateLeaf(plan.Leaves[index], leaf, partitions[index], owner, budget);
            if (policyEpoch is null)
            {
                policyEpoch = leaf.PolicyEpoch;
            }

            if (policyEpoch != leaf.PolicyEpoch)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, InconsistentOwnerMessage);
            }

            examined = checked(examined + leaf.ExaminedRecords);
            readBytes = checked(readBytes + leaf.ReadBytes);
            retained = checked(retained + leaf.RetainedBytes);
            if (examined > plan.MaxExaminedRecords || readBytes > plan.MaxReadBytes
                || retained > plan.MaxRetainedBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, "The partition query merge exceeds its reserved budget.");
            }
        }
        return (examined, readBytes, retained);
    }

    private static void ValidateLeaf(PartitionQueryLeafPlanV1 plan, PartitionQueryLeafResultV1? result,
        PartitionRef partition, StoreIdentity owner, ReadExecutionBudget budget)
    {
        ValidateLeafOwner(result, partition, owner);
        ValidateLeafShape(result!);
        ValidateLeafBudgets(plan, result!);
        ValidateCandidates(result!, plan, partition, budget);
        var expectedRetained = PartitionQueryRetention.CandidateArrayBytes(result!.Candidates.Length);
        foreach (var candidate in result.Candidates)
        {
            expectedRetained = checked(expectedRetained + PartitionQueryRetention.CandidateBytes(candidate));
        }

        if (result.RetainedBytes != expectedRetained)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidResultMessage);
        }
    }

    private static void ValidateLeafOwner(PartitionQueryLeafResultV1? result, PartitionRef partition,
        StoreIdentity owner)
    {
        if (result is null || result.Partition != partition || result.NodeId != owner.NodeId
            || result.Incarnation != owner.Incarnation || result.ReadGeneration != owner.ReadGeneration)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, InconsistentOwnerMessage);
        }
    }

    private static void ValidateLeafShape(PartitionQueryLeafResultV1 result)
    {
        if (result.Version != Version || result.CutPosition < 0 || result.PolicyEpoch < 1
            || result.SchemaVersion < 1 || result.ExaminedRecords < 0 || result.ReadBytes < 0
            || result.RetainedBytes < 0 || string.IsNullOrEmpty(result.AccessPath) || result.Candidates.IsDefault)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidResultMessage);
        }
    }

    private static void ValidateLeafBudgets(PartitionQueryLeafPlanV1 plan, PartitionQueryLeafResultV1 result)
    {
        if (result.Candidates.Length > plan.MaxCandidates || result.ExaminedRecords > plan.MaxExaminedRecords
            || result.ReadBytes > plan.MaxReadBytes || result.RetainedBytes > plan.MaxRetainedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, "The partition query leaf exceeded its reserved budget.");
        }
    }

    private static void ValidateCandidates(PartitionQueryLeafResultV1 result,
        PartitionQueryLeafPlanV1 plan, PartitionRef partition, ReadExecutionBudget budget)
    {
        PartitionQueryCandidateV1? previous = null;
        foreach (var candidate in result.Candidates)
        {
            budget.Check();
            if (candidate is null || candidate.Version != Version || candidate.Reference is null
                || candidate.Row is null || candidate.Reference.Partition != partition
                || candidate.Row.EntityId != candidate.Reference.Id || candidate.Row.Revision < 1
                || candidate.OrderKeys.IsDefault || candidate.OrderKeys.Length != plan.Request.Query.Order.Length
                || candidate.OrderKeys.Any(key => key.IsDefault))
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidResultMessage);
            }

            if (previous is not null && PartitionQueryOrder.Compare(previous, candidate, plan.Request.Query.Order) > 0)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidResultMessage);
            }

            previous = candidate;
        }
    }

    private static ImmutableArray<PartitionQueryCandidateV1> MergeRuns(
        ImmutableArray<PartitionQueryLeafResultV1> leaves,
        IReadOnlyList<Ordering> order, int maximumRows, ReadExecutionBudget budget)
    {
        var comparer = Comparer<PartitionQueryCandidateV1>.Create((left, right) =>
            PartitionQueryOrder.Compare(left, right, order));
        var queue = new PriorityQueue<MergeCursor, PartitionQueryCandidateV1>(comparer);
        var seen = new HashSet<EntityRef>();
        var candidateCount = 0;
        foreach (var leaf in leaves)
        {
            budget.Check();
            candidateCount = checked(candidateCount + leaf.Candidates.Length);
            foreach (var candidate in leaf.Candidates)
            {
                budget.Check();
                if (!seen.Add(candidate.Reference))
                {
                    throw Errors.Fail(ErrorCode.Corruption, DuplicateReferenceMessage);
                }
            }
        }
        for (var index = 0; index < leaves.Length; index++)
        {
            if (!leaves[index].Candidates.IsEmpty)
            {
                queue.Enqueue(new(index, 0), leaves[index].Candidates[0]);
            }
        }
        var output = ImmutableArray.CreateBuilder<PartitionQueryCandidateV1>(Math.Min(maximumRows, candidateCount));
        while (output.Count < maximumRows && queue.TryDequeue(out var cursor, out _))
        {
            budget.Check();
            output.Add(leaves[cursor.Leaf].Candidates[cursor.Index]);
            var next = cursor.Index + 1;
            if (next < leaves[cursor.Leaf].Candidates.Length)
            {
                queue.Enqueue(new(cursor.Leaf, next), leaves[cursor.Leaf].Candidates[next]);
            }
        }
        return output.MoveToImmutable();
    }

    private static long RetainedBytes(ImmutableArray<PartitionQueryLeafResultV1> leaves, int resultRows,
        ReadExecutionBudget budget)
    {
        long candidates = 0;
        var current = checked((long)PartitionQueryRetention.RootDescriptorBytes
            + (long)PartitionQueryRetention.LeafDescriptorBytes * leaves.Length
            + (long)PartitionQueryRetention.HeapEntryBytes * leaves.Length
            + PartitionQueryRetention.CandidateArrayBytes(resultRows));
        foreach (var leaf in leaves)
        {
            foreach (var candidate in leaf.Candidates)
            {
                budget.Check();
                candidates++;
                current = checked(current + PartitionQueryRetention.CandidateBytes(candidate));
            }
            current = checked(current + PartitionQueryRetention.CandidateArrayBytes(leaf.Candidates.Length));
        }
        return checked(current + PartitionQueryRetention.SeenReferenceEntryBytes * candidates);
    }

    private readonly record struct MergeCursor(int Leaf, int Index);
}
