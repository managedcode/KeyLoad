using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.QueryExecution;

internal static class PartitionQueryMerge
{
    private const int FirstElementIndex = 0;
    private const int EmptyElementCount = 0;
    private const int AdjacentElementOffset = 1;

    private const int Version = 1;
    private const string InconsistentOwnerMessage = "Partition query leaves do not share one owner and policy epoch.";
    private const string IncompleteMessage = "A partition query leaf did not complete.";

    internal static PartitionQueryResult Complete(PartitionQueryPlan plan,
        ImmutableArray<PartitionQueryLeafResult> leaves, StoreIdentity owner,
        ReadExecutionBudget budget, ImmutableArray<PhysicalShardRecord> physicalOwners = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(budget);
        if (leaves.IsDefault || leaves.Length != plan.Leaves.Length
            || !physicalOwners.IsDefault && physicalOwners.Length != leaves.Length)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, IncompleteMessage);
        }
        var policies = new Dictionary<Guid, (long PolicyEpoch, Guid NodeId, long ReadGeneration)>();
        for (var index = FirstElementIndex; index < leaves.Length; index++)
        {
            budget.Check();
            var leaf = leaves[index];
            var physical = physicalOwners.IsDefault ? null : physicalOwners[index];
            PartitionQueryLeafOwnerValidation.Validate(leaf, plan.Leaves[index].Partition, owner, physical);
            var policyOwner = physical?.PhysicalShardId ?? owner.NodeId;
            if (!policies.TryGetValue(policyOwner, out var policyEpoch))
            {
                policies.Add(policyOwner, (leaf.PolicyEpoch, leaf.NodeId, leaf.ReadGeneration));
            }
            else if (policyEpoch != (leaf.PolicyEpoch, leaf.NodeId, leaf.ReadGeneration))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, InconsistentOwnerMessage);
            }
        }
        var query = plan.Leaves[FirstElementIndex].Request.Query;
        return new(Version, true, leaves, MergeRuns(leaves, query.Order, plan.Limit, budget));
    }

    private static ImmutableArray<PartitionQueryCandidate> MergeRuns(
        ImmutableArray<PartitionQueryLeafResult> leaves,
        IReadOnlyList<Ordering> order, int maximumRows, ReadExecutionBudget budget)
    {
        var comparer = Comparer<PartitionQueryCandidate>.Create((left, right) =>
            PartitionQueryOrder.Compare(left, right, order));
        var queue = new PriorityQueue<MergeCursor, PartitionQueryCandidate>(comparer);
        var candidateCount = EmptyElementCount;
        foreach (var leaf in leaves)
        {
            candidateCount = checked(candidateCount + leaf.Candidates.Length);
        }
        for (var index = FirstElementIndex; index < leaves.Length; index++)
        {
            if (!leaves[index].Candidates.IsEmpty)
            {
                queue.Enqueue(new(index, EmptyElementCount), leaves[index].Candidates[FirstElementIndex]);
            }
        }
        var output = ImmutableArray.CreateBuilder<PartitionQueryCandidate>(Math.Min(maximumRows, candidateCount));
        while (output.Count < maximumRows && queue.TryDequeue(out var cursor, out _))
        {
            budget.Check();
            output.Add(leaves[cursor.Leaf].Candidates[cursor.Index]);
            var next = cursor.Index + AdjacentElementOffset;
            if (next < leaves[cursor.Leaf].Candidates.Length)
            {
                queue.Enqueue(new(cursor.Leaf, next), leaves[cursor.Leaf].Candidates[next]);
            }
        }
        return output.MoveToImmutable();
    }

    private readonly record struct MergeCursor(int Leaf, int Index);
}
