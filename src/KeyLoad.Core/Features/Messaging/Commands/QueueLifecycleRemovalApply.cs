using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void ApplyQueueLifecycleRemoval(IAtomicTransaction transaction, QueueLifecycleRemovalPlan plan)
    {
        transaction.Delete(plan.PrimaryKey);
        if (plan.SecondaryKey is { } secondary)
        {
            transaction.Delete(secondary);
        }
    }
}
