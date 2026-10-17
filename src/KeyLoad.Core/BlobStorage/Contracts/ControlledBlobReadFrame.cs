using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.BlobStorage;

[Orleans.GenerateSerializer, Orleans.Alias(ControlledBlobReadProtocol.FrameAlias)]
internal sealed record ControlledBlobReadFrame(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid QueryId,
    [property: Orleans.Id(2)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(3)] PartitionMovePublishedPlacement Publication,
    [property: Orleans.Id(4)] PrincipalRecord Principal,
    [property: Orleans.Id(5)] ResourceDefinition Resource,
    [property: Orleans.Id(6)] long DirectoryRevision,
    [property: Orleans.Id(7)] DateTimeOffset ExpiresAt,
    [property: Orleans.Id(8)] ControlledBlobReadPurpose Purpose,
    [property: Orleans.Id(9)] ReplicatedOperation? Original,
    [property: Orleans.Id(10)] StoredOutcome? OriginalOutcome,
    [property: Orleans.Id(11)] ReadOnlyMemory<byte> NativeRequest);
