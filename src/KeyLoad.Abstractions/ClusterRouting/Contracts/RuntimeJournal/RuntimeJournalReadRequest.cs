namespace KeyLoad;

/// <summary>One bounded private byte read pinned to the recovered journal instance and revision.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalAliases.Read)]
public sealed record RuntimeJournalReadRequest(
    [property: Orleans.Id(0)] string JournalName,
    [property: Orleans.Id(1)] Guid InstanceId,
    [property: Orleans.Id(2)] long OwnerGeneration,
    [property: Orleans.Id(3)] long ContentRevision,
    [property: Orleans.Id(4)] long Offset);
