namespace KeyLoad.Core.Features.Authorization;

internal readonly record struct RowVisibilityScope(
    Guid NodeId, Guid Incarnation, long ReadGeneration, long CutPosition,
    PartitionRef Partition, string Collection, long SchemaVersion,
    long DocumentEpoch, string PrincipalId, string PrincipalTenant, long PolicyEpoch)
{
    internal RowVisibilityCurrentScope Current => new(NodeId, Incarnation, ReadGeneration, CutPosition,
        Partition, Collection, SchemaVersion, PrincipalId, PrincipalTenant, PolicyEpoch);
    internal bool SameResourcePrincipal(RowVisibilityScope other)
        => NodeId == other.NodeId && Partition == other.Partition && Collection == other.Collection
            && PrincipalId == other.PrincipalId && PrincipalTenant == other.PrincipalTenant;
}
