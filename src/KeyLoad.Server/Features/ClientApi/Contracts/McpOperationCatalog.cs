using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace KeyLoad.Server;

/// <summary>The sole immutable version-one public database and agent tool inventory.</summary>
internal static class McpOperationCatalog
{
    /// <summary>All implemented public operations, excluding internal authentication and membership.</summary>
    internal static ImmutableArray<McpOperationDescriptor> Entries { get; } =
        [.. McpReadCatalog.Entries, .. McpCommandCatalog.Entries, .. McpSubscriptionCatalog.Entries,
            .. McpProjectionCatalog.Entries, .. KeyLoad.Server.Features.Search.OnlineTextMcpCatalog.Entries, .. BlobMcpCatalog.Entries, .. AdminDashboardMcpCatalog.Entries, SqlMcpCatalog.Entry];

    private static readonly FrozenDictionary<string, McpOperationDescriptor> Lookup =
        Entries.Where(entry => !entry.IsAdapter).ToFrozenDictionary(entry => entry.Name, StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, McpOperationDescriptor> Tools =
        Entries.ToFrozenDictionary(entry => entry.Name, StringComparer.Ordinal);

    internal static bool TryGetTool(string name, [NotNullWhen(true)] out McpOperationDescriptor? descriptor)
    {
        if (string.IsNullOrWhiteSpace(name))
        { descriptor = null; return false; }
        return Tools.TryGetValue(name, out descriptor);
    }

    /// <summary>Finds an exact stable tool name without trusting metadata supplied by a caller.</summary>
    /// <param name="name">Exact case-sensitive tool identity.</param>
    /// <param name="descriptor">Canonical immutable operation when present.</param>
    /// <returns>Whether the public version-one catalog contains the exact name.</returns>
    internal static bool TryGet(string name, [NotNullWhen(true)] out McpOperationDescriptor? descriptor)
    {
        if (string.IsNullOrWhiteSpace(name))
        { descriptor = null; return false; }
        return Lookup.TryGetValue(name, out descriptor);
    }
}
