using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Builds the fixed three-tool gateway entry page within the native response ceiling.</summary>
internal static class McpGatewayToolPagination
{
    private const string PageExceeded = "The gateway tool page exceeds the MCP response budget.";

    internal static ListToolsResult Create(string? cursor, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (!string.IsNullOrEmpty(cursor))
        { throw Errors.Fail(ErrorCode.Validation, McpGatewayMetaProtocol.InvalidCursor); }
        var page = new ListToolsResult();
        foreach (var tool in McpGatewayMetaProtocol.CreateTools())
        { page.Tools.Add(tool); }
        using var stream = new McpBoundedWriteStream(maximumBytes);
        try
        {
            JsonSerializer.Serialize(stream, page, McpJsonUtilities.DefaultOptions);
            return page;
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.ResourceExhausted)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, PageExceeded); }
    }
}
