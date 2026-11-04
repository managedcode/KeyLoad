using KeyLoad.Query.QueryExecution;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

/// <summary>Forwards one partition's bounded leaves to the exact storage address selected by its original request.</summary>
internal sealed class PartitionQueryGrain(GrainRequestCodec codec, SiloDatabaseClient storage,
    ILogger<PartitionQueryGrain> diagnostics) : Grain, IPartitionQueryGrain
{
    private const string NodeIdFormat = "D";
    private const long InitialReadGeneration = 0;

    public async Task<(SiloAddress Address, PhysicalShardRecord Owner)> ResolveOwnerAsync(Guid incarnation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (incarnation != codec.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        var address = await storage.ReadOwnerAsync(incarnation, cancellationToken);
        var status = await storage.On(address).ReadStatusAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (status.Incarnation != incarnation || status.ReadGeneration < InitialReadGeneration
            || !Guid.TryParseExact(status.NodeId, NodeIdFormat, out var nodeId) || nodeId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        return (address, new(nodeId, incarnation));
    }

    public async Task<GrainOperationReply> ExecuteAsync(SiloAddress owner, GrainRequestEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.Empty;
        try
        {
            ArgumentNullException.ThrowIfNull(owner) /* KLD0041: BCL null validation accepts the owner SiloAddress reference through its object parameter. */;
            cancellationToken.ThrowIfCancellationRequested();
            var request = codec.Decode(envelope);
            requestId = request.Envelope.RequestId;
            GrainIdentityContext.Validate(request.Envelope, requestId);
            if (!QueryLifetimeReadScope.Validate(request.Envelope))
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            }
            var partition = request.Envelope.ReadKind switch
            {
                GrainReadKind.PartitionQueryLeaf => (GrainPayload.Read<PartitionQueryOwnedLeafRequest>(
                    (request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.Query.QueryExecution.PartitionQueryOwnedLeafRequest at this closed operation boundary before executing typed code. */).Plan?.Partition,
                GrainReadKind.DistributedSearchLeaf => (GrainPayload.Read<DistributedSearchOwnedLeaf>(
                    (request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.Query.QueryExecution.DistributedSearchOwnedLeaf at this closed operation boundary before executing typed code. */).Search?.Partition,
                _ => null
            };
            if (partition is null || PartitionQueryGrainIdentity.For(partition) != this.GetPrimaryKeyString())
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            }
            return await storage.ExecuteAsync(owner, envelope, cancellationToken);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return GrainReplyFactory.Failure(error, false, diagnostics, requestId,
                GrainFailureStage.CapabilityExecution, cancellationToken);
        }
    }
}
