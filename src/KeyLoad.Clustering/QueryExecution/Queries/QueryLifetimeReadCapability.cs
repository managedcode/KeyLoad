using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Query;
using KeyLoad.Query.QueryExecution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class QueryLifetimeReadCapability
{
    internal static GrainValue Execute(DatabaseEngine database, IServiceProvider services,
        PrincipalRecord principal, DecodedGrainRequest request, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var owner = services.GetRequiredService<PhysicalShardRecord>();
        return request.Envelope.ReadKind switch
        {
            GrainReadKind.PartitionQueryLeaf => new GrainValue(PartitionQueryLifetimeLeafExecution.ExecuteBound(
                services.GetRequiredService<QueryEngine>(), principal.Id,
                (GrainPayload.Read<PartitionQueryOwnedLeafRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.Query.QueryExecution.PartitionQueryOwnedLeafRequest at this closed operation boundary before executing typed code. */), owner,
                binding, token)) /* KLD0041: The current GrainValue wire slot carries this DistributedSearchLeaf response with its generated runtime type header. */,
            GrainReadKind.DistributedSearchLeaf => new GrainValue(DistributedSearchOwnedLeafExecution.ExecuteBound(database,
                principal.Id, (GrainPayload.Read<DistributedSearchOwnedLeaf>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.Query.QueryExecution.DistributedSearchOwnedLeaf at this closed operation boundary before executing typed code. */), owner,
                services.GetRequiredService<IOptions<QueryExecutionOptions>>(),
                binding, token)) /* KLD0041: The current GrainValue wire slot carries this DistributedSearchLeaf response with its generated runtime type header. */,
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }
}
