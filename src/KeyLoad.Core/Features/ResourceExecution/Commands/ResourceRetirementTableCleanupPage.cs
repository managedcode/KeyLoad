using KeyLoad.Storage;
using static KeyLoad.Core.Features.ResourceExecution.ResourceRetirementTableCleanupFamilies;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Stages one bounded table-owned page; its caller persists Next in this same transaction.</summary>
internal static class ResourceRetirementTableCleanupPage
{
    internal static ResourceRetirementTableCleanupProgress Apply(IAtomicTransaction transaction,
        ResourceLifetimeV1 lifetime, PartitionRef partition, ResourceRetirementTableCleanupCursor cursor,
        DatabaseLimits limits, int reservedMutations, long reservedBytes)
    {
        var plan = Plan(transaction, lifetime, partition, cursor, limits, reservedMutations, reservedBytes);
        var selection = plan.Select(plan.Count);
        selection.Apply(transaction);
        return selection.Progress;
    }

    internal static ResourceRetirementTableCleanupPlan Plan(IKeyValueView view,
        ResourceLifetimeV1 lifetime, PartitionRef partition, ResourceRetirementTableCleanupCursor cursor,
        DatabaseLimits limits, int reservedMutations, long reservedBytes, bool stopAtCapacity = false)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(limits);
        var work = new ResourceRetirementTableCleanupBudget(limits, reservedMutations, reservedBytes, stopAtCapacity);
        var prefix = ResourceRetirementTableCleanupKeys.Prefix(lifetime, partition, cursor);
        if (cursor.FamilyOrdinal == All.Length)
        {
            return new(All.Length, [], [], false, Empty, Empty);
        }
        var family = All[cursor.FamilyOrdinal];
        var after = cursor.AfterKey;
        if (after.LongLength > limits.MaxBatchBytes - reservedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
        }
        var range = view.VisitRange(prefix, work.MaximumRecords, (key, value) =>
        {
            ResourceRetirementTableCleanupKeys.Require(key, prefix, family);
            return work.Retain(key, value);
        }, after.Length == Empty ? null : after, observer: work.Examine);
        work.RequireAccounting(range);
        var moreInFamily = range.HasMore || work.Stopped;
        return new(cursor.FamilyOrdinal, work.Keys, work.ValueBytes, moreInFamily,
            work.ExaminedBytes, work.ExaminedRecords);
    }
}
