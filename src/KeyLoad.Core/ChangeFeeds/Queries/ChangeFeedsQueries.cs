using System.Collections.Immutable;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.StoragePartitions.Queries;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes ChangeFeedsQueries operations against the caller's database context.</summary>
public static class ChangeFeedsQueries
{
    internal static ChangeFeedPage ReadChangeFeedBound(this DatabaseEngine engine, string principalId, ReadChangeFeedRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return engine.ReadChangeFeedRead(principalId, request, budget);
    }

    internal static ProjectionBatch ReadProjectionBatchBound(this DatabaseEngine engine, string principalId, ReadProjectionBatchRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return engine.Store.Read(raw =>
        {
            var result = engine.ReadProjectionBatchRead(budget.CreateView(raw), principalId, request);
            return result;
        });
    }

    internal static OutboxStatus GetOutboxStatusBound(this DatabaseEngine engine, string principalId, PartitionRef partition, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return engine.Store.Read(raw =>
        {
            var result = engine.GetOutboxStatusRead(budget.CreateView(raw), principalId, partition);
            return result;
        });
    }

    internal static void BindProjectionReadResources(IKeyValueView view, PartitionRef partition, ImmutableArray<string> names)
    {
        if (view is not IResourceLifetimeReadBindingView)
        {
            return;
        }

        if (names.Length == ProjectionOutbox.ProjectionAcceptsEmptyResourcesLength)
        {
            foreach (var resource in PartitionResourceCatalog.Capture(view, partition))
            {
                _ = ResourceLifetimeReadBinding.BindAuthorizedReadResource(view, partition, resource.Name);
            }

            return;
        }

        foreach (var name in names)
        {
            _ = ResourceLifetimeReadBinding.BindAuthorizedReadResource(view, partition, name);
        }
    }
}
