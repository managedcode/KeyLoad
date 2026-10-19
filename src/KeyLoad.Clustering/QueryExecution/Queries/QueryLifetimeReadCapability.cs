using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class QueryLifetimeReadCapability
{
    internal static object Execute(DatabaseEngine database, IServiceProvider services, TimeProvider clock,
        PrincipalRecord principal, DecodedGrainRequest request, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var owner = services.GetRequiredService<PhysicalShardRecord>();
        return request.Envelope.ReadKind switch
        {
            GrainReadKind.PartitionQueryLeaf => PartitionQueryLifetimeLeafExecution.ExecuteBound(
                services.GetRequiredService<QueryEngine>(), principal.Id,
                GrainPayload.Read<PartitionQueryOwnedLeafRequest>(request.Payload), owner, clock,
                request.Envelope.ExpiresAt, binding, token),
            GrainReadKind.DistributedSearchLeaf => DistributedSearchOwnedLeafExecution.ExecuteBound(database,
                principal.Id, GrainPayload.Read<DistributedSearchOwnedLeaf>(request.Payload), owner,
                services.GetRequiredService<IOptions<QueryExecutionOptions>>(), clock,
                request.Envelope.ExpiresAt, binding, token),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }
}
