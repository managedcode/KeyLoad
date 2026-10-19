namespace KeyLoad;

/// <summary>Reports the acquired execution generation's actual indexed prefix under current authorization.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(WaitForAnnIndexProtocol.ResultAlias)]
public sealed record WaitForAnnIndexResult(
    [property: Orleans.Id(0)] CommitToken IndexedToken,
    [property: Orleans.Id(1)] long IndexGeneration,
    [property: Orleans.Id(2)] long SchemaVersion,
    [property: Orleans.Id(3)] long PolicyEpoch);
