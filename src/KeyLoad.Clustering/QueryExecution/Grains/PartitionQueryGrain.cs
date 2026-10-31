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
        var address = await storage.ReadOwnerAsync(incarnation, cancellationToken).ConfigureAwait(true);
        var status = await storage.On(address).ReadStatusAsync(cancellationToken).ConfigureAwait(true);
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
            ArgumentNullException.ThrowIfNull(owner);
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
                GrainReadKind.PartitionQueryLeaf => GrainPayload.Read<PartitionQueryOwnedLeafRequest>(
                    request.Payload).Plan?.Partition,
                GrainReadKind.DistributedSearchLeaf => GrainPayload.Read<DistributedSearchOwnedLeaf>(
                    request.Payload).Search?.Partition,
                _ => null
            };
            if (partition is null || PartitionQueryGrainIdentity.For(partition) != this.GetPrimaryKeyString())
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            }
            return await storage.ExecuteAsync(owner, envelope, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return GrainReplyFactory.Failure(error, false, diagnostics, requestId,
                GrainFailureStage.CapabilityExecution, cancellationToken);
        }
    }
}
