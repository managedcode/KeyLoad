using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ChangeFeedPage ReadChangeFeedBound(string principalId, ReadChangeFeedRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return ReadChangeFeedRead(principalId, request, budget);
    }

    internal ProjectionBatch ReadProjectionBatchBound(string principalId, ReadProjectionBatchRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return Store.Read(raw =>
        {
            var result = ReadProjectionBatchRead(budget.CreateView(raw), principalId, request);
            budget.CheckResult(result);
            return result;
        });
    }

    internal OutboxStatus GetOutboxStatusBound(string principalId, PartitionRef partition,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return Store.Read(raw =>
        {
            var result = GetOutboxStatusRead(budget.CreateView(raw), principalId, partition);
            budget.CheckResult(result);
            return result;
        });
    }

    private void BindProjectionReadResources(IKeyValueView view, PartitionRef partition, ImmutableArray<string> names)
    {
        if (view is not IResourceLifetimeReadBindingView)
        {
            return;
        }
        if (names.Length == ProjectionAcceptsEmptyResourcesLength)
        {
            foreach (var resource in PartitionResourceCatalog.Capture(view, partition, Limits))
            {
                _ = BindAuthorizedReadResource(view, partition, resource.Name);
            }
            return;
        }
        foreach (var name in names)
        {
            _ = BindAuthorizedReadResource(view, partition, name);
        }
    }
}
