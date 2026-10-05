using System.Collections.Immutable;

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

/// <summary>One owned opaque page; the caller never receives a native storage handle.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalAliases.Page)]
public sealed record RuntimeJournalPage(
    [property: Orleans.Id(0)] ReadOnlyMemory<byte> Data,
    [property: Orleans.Id(1)] bool IsCompleted);

/// <summary>The complete admitted journal catalog for the initial RF3 authority group.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalAliases.Catalog)]
public sealed record RuntimeJournalCatalog(
    [property: Orleans.Id(0)] ImmutableArray<RuntimeJournalSnapshot> Journals);
