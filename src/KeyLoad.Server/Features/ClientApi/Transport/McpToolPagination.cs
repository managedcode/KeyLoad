using System.Globalization;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Builds bounded native discovery pages from the sole immutable catalog without sharing mutable tools.</summary>
internal static class McpToolPagination
{
    private const string CursorPrefix = "keyload-mcp-v1:";
    private const string InvalidCursor = "The MCP discovery cursor is invalid.";
    private const string ToolBudgetExceeded = "A catalog tool exceeds the MCP discovery page budget.";
    private const int MaximumIndexDigits = 10;

    /// <summary>Measures each complete candidate, including its next cursor, using the actual public native serializer.</summary>
    /// <param name="cursor">The canonical next catalog position, or null to start at the first entry.</param>
    /// <param name="maximumBytes">The inclusive page ceiling and private writer capacity reserved by the caller.</param>
    /// <returns>A nonempty fresh native page with its exact continuation cursor, null at catalog end.</returns>
    /// <exception cref="KeyLoadException">The cursor is invalid or a single complete tool cannot fit.</exception>
    internal static ListToolsResult Create(string? cursor, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        var index = ReadIndex(cursor);
        var page = new ListToolsResult();
        for (; index < McpOperationCatalog.Entries.Length; index++)
        {
            page.Tools.Add(McpOperationCatalog.Entries[index].CreateTool());
            page.NextCursor = index + 1 < McpOperationCatalog.Entries.Length ? CursorFor(index + 1) : null;
            if (Fits(page, maximumBytes))
            { continue; }
            page.Tools.RemoveAt(page.Tools.Count - 1);
            if (page.Tools.Count == 0)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, ToolBudgetExceeded); }
            page.NextCursor = CursorFor(index);
            return page;
        }
        return page;
    }

    private static int ReadIndex(string? cursor)
    {
        if (cursor is null)
        { return 0; }
        if (!cursor.StartsWith(CursorPrefix, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Validation, InvalidCursor); }
        var digits = cursor.AsSpan(CursorPrefix.Length);
        if (digits.IsEmpty || digits.Length > MaximumIndexDigits || digits[0] == '0'
            || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || index <= 0 || index >= McpOperationCatalog.Entries.Length)
        { throw Errors.Fail(ErrorCode.Validation, InvalidCursor); }
        return index;
    }

    private static string CursorFor(int index) => CursorPrefix + index.ToString(CultureInfo.InvariantCulture);

    private static bool Fits(ListToolsResult page, int maximumBytes)
    {
        using var stream = new McpBoundedWriteStream(maximumBytes);
        try
        {
            JsonSerializer.Serialize(stream, page, McpJsonUtilities.DefaultOptions);
            return true;
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.ResourceExhausted)
        {
            return false;
        }
    }
}
