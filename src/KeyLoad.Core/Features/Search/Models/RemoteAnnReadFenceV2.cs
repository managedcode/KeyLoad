using KeyLoad.Core.Features.Search;

namespace KeyLoad.Core;

internal sealed record RemoteAnnReadFenceV2(RemoteAnnReadFence? Original,
    AnnReadLifetimeMetadata Metadata, Guid SourceNodeId, long SourceReadGeneration,
    RegisteredPhysicalOwnerV1 ConfiguredLocal);
