namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.DocumentReadFrameAlias)]
internal sealed record PartitionControlDocumentReadFrame(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid QueryId,
    [property: Orleans.Id(2)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(3)] PartitionMovePublishedPlacement Publication,
    [property: Orleans.Id(4)] PrincipalRecord Principal,
    [property: Orleans.Id(5)] ResourceDefinition Resource,
    [property: Orleans.Id(6)] EntityRef Reference,
    [property: Orleans.Id(7)] CommitToken? MinimumToken,
    [property: Orleans.Id(8)] DateTimeOffset ExpiresAt,
    [property: Orleans.Id(9)] long DirectoryRevision);
