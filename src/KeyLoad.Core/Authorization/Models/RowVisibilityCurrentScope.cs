namespace KeyLoad.Core.Features.Authorization;

internal readonly record struct RowVisibilityCurrentScope(
    Guid NodeId, Guid Incarnation, long ReadGeneration, long CutPosition,
    PartitionRef Partition, string Collection, long SchemaVersion,
    string PrincipalId, string PrincipalTenant, long PolicyEpoch);
