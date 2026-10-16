using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

internal sealed record PartitionQuerySourceFenceV2(PartitionQuerySourceFence Original,
    PartitionQueryLifetimeMetadata Metadata)
{
    internal RemoteDocumentReadFenceV1 Authority => Original.Authority;
}

