using KeyLoad.Core;

namespace KeyLoad.Query;

/// <summary>Dispatches scalar and Q2 reads inside the existing single query view.</summary>
internal static class RelationalInnerJoinDispatch
{
    internal static QueryPage Execute(QueryEngine owner, string principalId, AstQueryRequest request,
        string hash, ReadExecutionBudget budget, TimeProvider clock)
    {
        var database = owner.PartitionQueryOwner;
        QueryReadCutObservation? observation = null;
        var result = database.WithQueryView(principalId, request.Partition, request.Query.Collection, Capability.None,
            (view, principal, leftResource) =>
            {
                if (request.Query.InnerJoin is not { } join)
                {
                    var page = owner.ExecuteView(view, principal, leftResource, request, hash, budget, clock);
                    observation = (owner.CaptureQueryReadCut(principal, leftResource, request, hash, page, budget)).Value;
                    return page;
                }
                database.Authorization.Require(principal, request.Partition, join.Collection,
                    Capability.Query | Capability.DocumentsRead);
                var rightResource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(budget.CreateView(view), request.Partition, join.Collection, ResourceKind.Collection);
                budget.CaptureResourceLifetimes(budget.CreateView(view), request.Partition,
                    leftResource.Name == rightResource.Name ? [leftResource] : [leftResource, rightResource]);
                return RelationalInnerJoinExecutor.Execute(database, view, principal, leftResource, rightResource,
                    request, budget);
            }, budget);
        owner.PublishQueryReadCut(observation, budget);
        return result;
    }
}
