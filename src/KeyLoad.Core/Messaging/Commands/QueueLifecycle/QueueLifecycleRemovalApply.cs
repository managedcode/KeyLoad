using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes QueueLifecycleRemovalApply operations against the caller's database context.</summary>
public static class QueueLifecycleRemovalApply
{
    internal static void ApplyQueueLifecycleRemoval(IAtomicTransaction transaction, QueueLifecycleRemovalPlan plan)
    {
        transaction.Delete(plan.PrimaryKey);
        if (plan.SecondaryKey is { } secondary)
        {
            transaction.Delete(secondary);
        }
    }
}
