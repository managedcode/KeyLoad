using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.QueryExecution;

internal static partial class DistributedSearchOwnedLeafExecution
{
    internal static DistributedSearchLeafResult ExecuteBound(DatabaseEngine database, string principalId,
        DistributedSearchOwnedLeaf request, PhysicalShardRecord actualOwner,
        IOptions<QueryExecutionOptions> options, TimeProvider clock,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return Execute(database, principalId, request, actualOwner, options, clock, token, binding);
    }
}
