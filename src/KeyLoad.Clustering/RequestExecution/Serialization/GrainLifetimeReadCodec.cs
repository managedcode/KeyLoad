namespace KeyLoad.Clustering;

/// <summary>Builds the current lifetime read envelopes before the original codec signs them.</summary>
internal static class GrainLifetimeReadCodec
{
    internal static GrainRequestEnvelope CreatePartitionQueryLeaf(this GrainRequestCodec codec, Guid requestId, string principalId,
        object payload)
    {
        return codec.Issue(GrainRequestCapabilityEnvelopes.RemoteRead(codec, requestId, principalId,
            GrainReadKind.PartitionQueryLeaf, payload)
            with
        {
            Purpose = QueryLifetimeReadProtocol.Purpose
        });
    }

    internal static GrainRequestEnvelope CreateDistributedSearchLeaf(this GrainRequestCodec codec, Guid requestId, string principalId,
        object payload)
    {
        return codec.Issue(GrainRequestCapabilityEnvelopes.RemoteRead(codec, requestId, principalId,
            GrainReadKind.DistributedSearchLeaf, payload)
            with
        {
            Purpose = QueryLifetimeReadProtocol.Purpose
        });
    }
}
