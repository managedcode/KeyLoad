namespace KeyLoad.Clustering;

/// <summary>Builds the current lifetime read envelopes before the original codec signs them.</summary>
internal static class GrainLifetimeReadCodec
{
    internal static string CreatePartitionQueryLeaf(this GrainRequestCodec codec, Guid requestId, string principalId,
        ReadOnlyMemory<byte> payload, DateTimeOffset expiresAt)
    {
        var context = codec.LifetimeReadContext;
        return codec.Issue(GrainRequestCapabilityEnvelopes.RemoteRead(context.Scope, context.Now,
            context.Lifetime, requestId, principalId, GrainReadKind.PartitionQueryLeaf, payload, expiresAt)
            with
        {
            Purpose = QueryLifetimeReadProtocol.Purpose
        });
    }

    internal static string CreateDistributedSearchLeaf(this GrainRequestCodec codec, Guid requestId, string principalId,
        ReadOnlyMemory<byte> payload, DateTimeOffset expiresAt)
    {
        var context = codec.LifetimeReadContext;
        return codec.Issue(GrainRequestCapabilityEnvelopes.RemoteRead(context.Scope, context.Now,
            context.Lifetime, requestId, principalId, GrainReadKind.DistributedSearchLeaf, payload, expiresAt)
            with
        {
            Purpose = QueryLifetimeReadProtocol.Purpose
        });
    }
}
