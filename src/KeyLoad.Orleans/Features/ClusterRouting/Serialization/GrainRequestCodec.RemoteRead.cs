
namespace KeyLoad.Orleans;

public sealed partial class GrainRequestCodec
{
    /// <summary>Signs a receiving child without extending its admitted source expiry.</summary>
    /// <param name="requestId">Unique receiving actor identity.</param>
    /// <param name="principalId">Fresh receiving persisted principal identity.</param>
    /// <param name="payload">Exact receiving-only document payload.</param>
    /// <param name="expiresAt">Original admitted source request deadline.</param>
    /// <returns>The locally signed native receiving request.</returns>
    public string CreateOwnedDocumentRead(Guid requestId, string principalId,
        ReadOnlyMemory<byte> payload, DateTimeOffset expiresAt)
        => Issue(GrainRequestCapabilityEnvelopes.RemoteRead(database, clock.GetUtcNow(),
            settings.RequestLifetime, requestId, principalId, GrainReadKind.OwnedDocument, payload, expiresAt));

    internal string CreatePartitionQueryLeaf(Guid requestId, string principalId,
        ReadOnlyMemory<byte> payload, DateTimeOffset expiresAt)
        => Issue(GrainRequestCapabilityEnvelopes.RemoteRead(database, clock.GetUtcNow(),
            settings.RequestLifetime, requestId, principalId, GrainReadKind.PartitionQueryLeaf, payload, expiresAt));
}
