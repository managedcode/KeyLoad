namespace KeyLoad.Core;

internal sealed record RemoteAnnReadFence(string PrincipalId, string TenantId, long PolicyEpoch,
    Guid SourceNodeId, long SourceReadGeneration, long? DirectoryRevision,
    string ResourceSha256, string ConsumerSha256, RegisteredPhysicalOwnerV1 ConfiguredLocal,
    RegisteredPhysicalOwnerV1 Destination,
    AnnMaintenanceRequest OriginalMaintenance);
