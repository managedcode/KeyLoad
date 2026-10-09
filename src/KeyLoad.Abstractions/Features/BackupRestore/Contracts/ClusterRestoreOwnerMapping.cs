using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Explicit operator configuration; no credential, key or database role is carried here.</summary>
/// <param name="Version">Independent mapping schema version.</param>
/// <param name="Source">Exact verified original owner tuple.</param>
/// <param name="Target">New configured RF3 tuple, never an original signed authority.</param>
/// <param name="Endpoints">Exact ordered target server bases matching target voter order.</param>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterRestoreOwnerMapping.SerializerAlias)]
public sealed record ClusterRestoreOwnerMapping(
    [property: Orleans.Id(ClusterRestoreOwnerMapping.VersionField)] int Version,
    [property: Orleans.Id(ClusterRestoreOwnerMapping.SourceField)] PhysicalShardRecord Source,
    [property: Orleans.Id(ClusterRestoreOwnerMapping.TargetField)] PhysicalShardRecord Target,
    [property: Orleans.Id(ClusterRestoreOwnerMapping.EndpointsField)] ImmutableArray<string> Endpoints)
{
    internal const string SerializerAlias = "keyload.backup.cluster-restore-owner-mapping.v1";
    /// <summary>Current mapping schema.</summary>
    public const int CurrentVersion = 1;
    private const int VersionField = 0;
    private const int SourceField = 1;
    private const int TargetField = 2;
    private const int EndpointsField = 3;
}
