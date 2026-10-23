using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>A server-issued, bounded journal mutation behind the private request purpose.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalAliases.Mutation)]
public sealed record RuntimeJournalMutation(
    [property: Orleans.Id(0)] RuntimeJournalAction Action,
    [property: Orleans.Id(1)] string JournalName,
    [property: Orleans.Id(2)] Guid InstanceId,
    [property: Orleans.Id(3)] long OwnerGeneration,
    [property: Orleans.Id(4)] long ContentRevision,
    [property: Orleans.Id(5)] string? ExpectedMetadataETag,
    [property: Orleans.Id(6)] ReadOnlyMemory<byte> Data,
    [property: Orleans.Id(7)] Dictionary<string, string> SetProperties,
    [property: Orleans.Id(8)] ImmutableArray<string> RemoveProperties);
