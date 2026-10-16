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
        PrincipalRecord principal, DecodedGrainRequest request, CancellationToken token, IResourceLifetimeWorkBinding binding)
    {
        token.ThrowIfCancellationRequested();
        var owner = services.GetRequiredService<PhysicalShardRecord>();
        return request.Envelope.ReadKind switch
        {
            GrainReadKind.PartitionQueryLeafV2 => PartitionQueryLifetimeLeafExecution.ExecuteBound(
                services.GetRequiredService<QueryEngine>(), principal.Id,
                GrainNativePayload.Read<PartitionQueryOwnedLeafRequestV2>(request.Payload), owner, clock,
                request.Envelope.ExpiresAt, token, binding),
            GrainReadKind.DistributedSearchLeafV2 => DistributedSearchOwnedLeafExecution.ExecuteV2Bound(database,
                principal.Id, GrainNativePayload.Read<DistributedSearchOwnedLeafV2>(request.Payload), owner,
                services.GetRequiredService<IOptions<QueryExecutionOptions>>(), clock,
                request.Envelope.ExpiresAt, token, binding),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }
}
