using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core;

/// <summary>Authenticated source metadata for one addressed query read; it owns no storage or authority.</summary>
internal sealed record PartitionQueryLifetimeMetadata(ResourceDefinition Definition,
    ResourceLifetimeCoverageV1 Coverage, PrincipalRecord Principal);
