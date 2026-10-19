namespace KeyLoad;

/// <summary>A bounded private journal header; it grants no user database authority.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalAliases.Snapshot)]
public sealed record RuntimeJournalSnapshot(
    [property: Orleans.Id(0)] string JournalName,
    [property: Orleans.Id(1)] Guid InstanceId,
    [property: Orleans.Id(2)] long OwnerGeneration,
    [property: Orleans.Id(3)] long ContentRevision,
    [property: Orleans.Id(4)] long Length,
    [property: Orleans.Id(5)] string MetadataETag,
    [property: Orleans.Id(6)] Dictionary<string, string> Properties);
