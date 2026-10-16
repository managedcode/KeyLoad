using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.QueryExecution;

internal static partial class DistributedSearchOwnedLeafExecution
{
    internal static DistributedSearchLeafResultV1 ExecuteV2Bound(DatabaseEngine database, string principalId,
        DistributedSearchOwnedLeafV2 request, PhysicalShardRecord actualOwner,
        IOptions<QueryExecutionOptions> options, TimeProvider clock, DateTimeOffset originalExpiry,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return ExecuteV2(database, principalId, request, actualOwner, options, clock, originalExpiry, token, binding);
    }
}
