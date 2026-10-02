using System.Globalization;
using System.Text.Json;
using KeyLoad.Server;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Owns the strict cursor vectors and actual native page serializer used by pagination regressions.</summary>
internal static class McpPaginationTestData
{
    internal const int CatalogCount = 37;
    internal const int FinalIndex = CatalogCount - 1;
    internal const int MaximumPageBytes = 65_536;
    internal const string Prefix = "keyload-mcp-v1:";
    internal const string Marker = "private-cursor-marker";
    internal const string EmptyCursor = "";
    internal const string ZeroCursor = Prefix + "0";
    internal const string LeadingZeroCursor = Prefix + "01";
    internal const string PlusCursor = Prefix + "+1";
    internal const string MinusCursor = Prefix + "-1";
    internal const string LeadingSpaceCursor = Prefix + " 1";
    internal const string TrailingSpaceCursor = Prefix + "1 ";
    internal const string CaseCursor = "KeyLoad-mcp-v1:1";
    internal const string FractionCursor = Prefix + "1.0";
    internal const string NonAsciiCursor = Prefix + "١";
    internal const string EndCursor = Prefix + "37";
    internal const string IntegerMaximumCursor = Prefix + "2147483647";
    internal const string OverflowCursor = Prefix + "999999999999999999999999999999999999";
    internal const string MarkerCursor = Prefix + Marker;

    /// <summary>Formats the specified position independently using the frozen wire prefix and invariant decimal.</summary>
    /// <param name="index">The exact positive cursor position.</param>
    /// <returns>A canonical native discovery cursor.</returns>
    internal static string Cursor(int index) => Prefix + index.ToString(CultureInfo.InvariantCulture);

    /// <summary>Builds an actual singleton native page with its required next-position metadata.</summary>
    /// <param name="index">The sole catalog entry to include.</param>
    /// <returns>The complete candidate used to establish a real serialized byte ceiling.</returns>
    internal static ListToolsResult Singleton(int index) => new()
    {
        Tools = [McpOperationCatalog.Entries[index].CreateTool()],
        NextCursor = index == FinalIndex ? null : Cursor(index + 1)
    };

    /// <summary>Uses the public official serializer for the full result, including its cursor.</summary>
    /// <param name="page">The actual native result object.</param>
    /// <returns>Actual serialized bytes used only for regression assertions.</returns>
    internal static byte[] NativeBytes(ListToolsResult page) => JsonSerializer.SerializeToUtf8Bytes(page, McpJsonUtilities.DefaultOptions);
}
