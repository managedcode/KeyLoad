namespace KeyLoad;

/// <summary>Server-derived registration input; no public route accepts this trusted payload.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PhysicalOwnerDirectoryAliases.Register)]
internal sealed record RegisterPhysicalOwnerV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] long ExpectedRevision,
    [property: Orleans.Id(2)] RegisteredPhysicalOwnerV1 Control,
    [property: Orleans.Id(3)] RegisteredPhysicalOwnerV1 Destination);
