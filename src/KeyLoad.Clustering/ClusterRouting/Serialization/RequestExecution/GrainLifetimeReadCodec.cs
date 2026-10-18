namespace KeyLoad.Clustering;

/// <summary>Builds the versioned lifetime read envelopes before the original codec signs them.</summary>
internal static class GrainLifetimeReadCodec
{
    internal static string CreatePartitionQueryLeafV2(this GrainRequestCodec codec, Guid requestId, string principalId,
        ReadOnlyMemory<byte> payload, DateTimeOffset expiresAt)
    {
        var context = codec.LifetimeReadContext;
        return codec.Issue(GrainRequestCapabilityEnvelopes.RemoteRead(context.Database, context.Now,
            context.Lifetime, requestId, principalId, GrainReadKind.PartitionQueryLeafV2, payload, expiresAt)
            with
        {
            Purpose = QueryLifetimeReadProtocol.Purpose
        });
    }

    internal static string CreateDistributedSearchLeafV2(this GrainRequestCodec codec, Guid requestId, string principalId,
        ReadOnlyMemory<byte> payload, DateTimeOffset expiresAt)
    {
        var context = codec.LifetimeReadContext;
        return codec.Issue(GrainRequestCapabilityEnvelopes.RemoteRead(context.Database, context.Now,
            context.Lifetime, requestId, principalId, GrainReadKind.DistributedSearchLeafV2, payload, expiresAt)
            with
        {
            Purpose = QueryLifetimeReadProtocol.Purpose
        });
    }

    internal static string CreateAnnProjectionObservationV2(this GrainRequestCodec codec, Guid requestId, string principalId,
        ReadOnlyMemory<byte> payload, DateTimeOffset expiresAt)
    {
        var context = codec.LifetimeReadContext;
        var envelope = GrainRequestCapabilityEnvelopes.RemoteRead(context.Database, context.Now,
            context.Lifetime, requestId, principalId, GrainReadKind.AnnProjectionObservationV2, payload, expiresAt);
        return codec.Issue(envelope with
        {
            Purpose = AnnProjectionLifetimeObservationScope.Purpose
        });
    }
}
