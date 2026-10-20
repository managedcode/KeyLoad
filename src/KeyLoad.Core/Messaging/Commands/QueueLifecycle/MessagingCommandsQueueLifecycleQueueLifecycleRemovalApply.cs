using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingCommandsQueueLifecycleQueueLifecycleRemovalApply operations against the caller's database context.</summary>
public static class MessagingCommandsQueueLifecycleQueueLifecycleRemovalApply
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
