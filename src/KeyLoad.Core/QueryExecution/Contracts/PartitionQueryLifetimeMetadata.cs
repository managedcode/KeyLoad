using KeyLoad.Core.Features.StoragePartitions.Contracts;

namespace KeyLoad.Core;

/// <summary>Authenticated source metadata for one addressed query read; it owns no storage or authority.</summary>
internal sealed record PartitionQueryLifetimeMetadata(ResourceDefinition Definition,
    ResourceLifetimeCoverage Coverage, PrincipalRecord Principal);
