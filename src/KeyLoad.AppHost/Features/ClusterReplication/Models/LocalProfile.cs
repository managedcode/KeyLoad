namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Private immutable shared RF3 identity and development credentials.</summary>
/// <param name="Version">Version of the strict Aspire profile schema.</param>
/// <param name="PhysicalShardId">Opaque physical shard identity shared by every configured voter.</param>
/// <param name="Incarnation">Database incarnation preserved across node restarts.</param>
/// <param name="SigningKey">Base64 shared database signing key.</param>
/// <param name="PeerSecret">Base64 shared authenticated peer key.</param>
/// <param name="AdminKey">Initial persisted root API key.</param>
internal sealed record LocalProfile(int Version, Guid PhysicalShardId, Guid Incarnation,
    string SigningKey, string PeerSecret, string AdminKey);
