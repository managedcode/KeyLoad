namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(FollowerDocumentInternalAliases.Capability)]
internal sealed record FollowerDocumentReadCapability(
    [property: Orleans.Id(0)] ReadFollowerDocumentRequestV1 Request,
    [property: Orleans.Id(1)] DatabaseCredentialWitness Credential);

[Orleans.GenerateSerializer]
[Orleans.Alias(FollowerDocumentInternalAliases.Snapshot)]
internal sealed record FollowerDocumentSnapshot(
    [property: Orleans.Id(0)] DocumentRecord? Record,
    [property: Orleans.Id(1)] CommitToken Token,
    [property: Orleans.Id(2)] Guid NodeId,
    [property: Orleans.Id(3)] long ReadGeneration,
    [property: Orleans.Id(4)] PhysicalShardRecord Owner,
    [property: Orleans.Id(5)] string ReplicaId,
    [property: Orleans.Id(6)] long Term);
