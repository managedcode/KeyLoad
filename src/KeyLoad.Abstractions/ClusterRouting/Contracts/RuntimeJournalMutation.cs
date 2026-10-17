using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Closed private infrastructure actions; these are not public database tools.</summary>
public enum RuntimeJournalAction
{
    /// <summary>Creates the protected journal identity under current administrator authority.</summary>
    BootstrapIdentity,
    /// <summary>Creates a journal if its name is absent.</summary>
    Create,
    /// <summary>Changes bounded native metadata using full ETag comparison.</summary>
    UpdateMetadata,
    /// <summary>Appends opaque bytes at a captured owner and content revision.</summary>
    Append,
    /// <summary>Replaces opaque bytes at a captured owner and content revision.</summary>
    Replace,
    /// <summary>Deletes a captured journal instance and releases its capacity.</summary>
    Delete
}

/// <summary>A server-issued, bounded journal mutation behind the private signed request purpose.</summary>
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
