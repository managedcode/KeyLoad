using KeyLoad.Core;

namespace KeyLoad.Query;

/// <summary>Dispatches scalar and Q2 reads inside the existing single query view.</summary>
internal static class RelationalInnerJoinDispatch
{
    internal static QueryPage Execute(QueryEngine owner, string principalId, AstQueryRequest request,
        string hash, ReadExecutionBudget budget, TimeProvider clock)
    {
        var database = owner.PartitionQueryOwner;
        return database.WithQueryView(principalId, request.Partition, request.Query.Collection,
            (view, principal, leftResource) =>
            {
                if (request.Query.InnerJoin is not { } join)
                {
                    return owner.ExecuteView(view, principal, leftResource, request, hash, budget, clock);
                }
                database.Authorization.Require(principal, request.Partition, join.Collection,
                    Capability.Query | Capability.DocumentsRead);
                var rightResource = database.Resource(view, request.Partition, join.Collection, ResourceKind.Collection);
                return RelationalInnerJoinExecutor.Execute(database, view, principal, leftResource, rightResource,
                    request, budget);
            });
    }
}
