namespace KeyLoad;

/// <summary>Server-only source fence; contains identity scope and committed routing, never grants.</summary>
/// <param name="PrincipalId">Fresh source subject.</param>
/// <param name="Tenant">Fresh source tenant.</param>
/// <param name="PolicyEpoch">Fresh source policy epoch.</param>
/// <param name="Placement">Complete explicit PMAP witness.</param>
/// <param name="DirectoryRevision">Registered owner directory revision.</param>
/// <param name="Destination">Exact configured receiving owner and endpoints.</param>
[Orleans.GenerateSerializer, Orleans.Alias(RemoteDocumentAliases.Fence)]
public sealed record RemoteDocumentReadFenceV1(
    [property: Orleans.Id(0)] string PrincipalId,
    [property: Orleans.Id(1)] string Tenant,
    [property: Orleans.Id(2)] long PolicyEpoch,
    [property: Orleans.Id(3)] AtomicPartitionPlacementResolution Placement,
    [property: Orleans.Id(4)] long DirectoryRevision,
    [property: Orleans.Id(5)] RegisteredPhysicalOwnerV1 Destination);
