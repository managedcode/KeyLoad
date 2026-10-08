using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Owns one complete reply wrapper and its borrowed native structured-content document until response draining.</summary>
internal sealed class McpReplyOwner : IDisposable
{
    private const int ThrowIfDisposedEmptyRead = 0;

    private readonly byte[] bytes;
    private readonly JsonDocument document;
    private readonly bool isError;
    private int disposed;

    private McpReplyOwner(byte[] bytes, bool isError)
    {
        this.bytes = bytes;
        this.isError = isError;
        document = JsonDocument.Parse(bytes);
    }

    /// <summary>Gets borrowed complete wrapper bytes whose lifetime and clearing remain owned here.</summary>
    internal ReadOnlyMemory<byte> Bytes
    {
        get { ThrowIfDisposed(); return bytes; }
    }

    /// <summary>Checks and wraps the exact canonical result after the caller reserves the complete output allocation.</summary>
    /// <param name="canonical">The borrowed complete canonical JSON value.</param>
    /// <param name="requestId">The actual database execution identity.</param>
    /// <param name="maximumBytes">The inclusive complete wrapper ceiling and private writer capacity.</param>
    /// <param name="options">The centrally validated native MCP framing policy.</param>
    /// <returns>An owner that must survive native response serialization and draining.</returns>
    internal static McpReplyOwner Success(ReadOnlyMemory<byte> canonical, Guid? requestId, int maximumBytes,
        IOptions<McpExecutionOptions> options)
    {
        _ = McpFrameBounds.InspectReply(canonical.Span, maximumBytes, options);
        return Open(McpReplyWriter.Success(canonical.Span, requestId, maximumBytes), isError: false);
    }

    /// <summary>Wraps a fresh standard problem using only its code and owned safe detail after output reservation.</summary>
    /// <param name="code">The database error category, never an exception or caller message.</param>
    /// <param name="requestId">The actual execution identity, or null before database dispatch.</param>
    /// <param name="maximumBytes">The inclusive complete wrapper ceiling and private writer capacity.</param>
    /// <param name="ownedDetail">Actual server diagnostic considered only by the closed safe mapping.</param>
    /// <returns>An owner for the fixed safe failure response.</returns>
    internal static McpReplyOwner Failure(ErrorCode code, Guid? requestId, int maximumBytes, string? ownedDetail = null)
        => Open(McpReplyWriter.Failure(code, requestId, maximumBytes, ownedDetail), isError: true);

    /// <summary>Creates fresh native result and text objects while borrowing this owner's structured-content element.</summary>
    /// <returns>A native tool result whose document must not outlive this owner.</returns>
    internal CallToolResult ToolResult()
    {
        ThrowIfDisposed();
        return new CallToolResult
        {
            StructuredContent = document.RootElement,
            IsError = isError,
            Content = [new TextContentBlock { Text = isError ? McpReplyProtocol.FailureSummary : McpReplyProtocol.SuccessSummary }]
        };
    }

    /// <summary>Closes the borrowed document and clears the complete owned UTF-8 array exactly once.</summary>
    public void Dispose()
    {
        const int ValueSingleItemCount = 1;
        const int EmptyExchange = 0;

        if (Interlocked.Exchange(ref disposed, ValueSingleItemCount) != EmptyExchange)
        { return; }
        try
        { document.Dispose(); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static McpReplyOwner Open(byte[] bytes, bool isError)
    {
        var transferred = false;
        try
        {
            var owner = new McpReplyOwner(bytes, isError);
            transferred = true;
            return owner;
        }
        finally
        {
            if (!transferred)
            { CryptographicOperations.ZeroMemory(bytes); }
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != ThrowIfDisposedEmptyRead, this);
}
