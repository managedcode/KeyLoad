namespace KeyLoad.Clustering;

internal static class GrainResourceOwnerInventoryCodec
{
    internal static string CreateResourceOwnerInventoryRead(this GrainRequestCodec codec,
        Guid requestId, string verifiedPrincipalId, ReadOnlyMemory<byte> payload, DateTimeOffset expiresAt)
    {
        var context = codec.LifetimeReadContext;
        var envelope = GrainRequestCapabilityEnvelopes.RemoteRead(context.Database, context.Now,
            context.Lifetime, requestId, verifiedPrincipalId, GrainReadKind.ResourceOwnerInventory, payload, expiresAt);
        return codec.Issue(envelope with
        {
            Purpose = ResourceOwnerInventoryReadScope.Purpose
        });
    }
}
