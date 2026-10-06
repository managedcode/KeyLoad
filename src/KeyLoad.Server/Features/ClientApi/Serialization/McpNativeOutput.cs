using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Checks actual native polymorphic response serialization after SDK metadata and before SSE output.</summary>
internal static class McpNativeOutput
{
    /// <summary>Serializes into one bounded private capacity and inspects only its borrowed written range.</summary>
    /// <param name="message">The genuine native response with any already injected metadata.</param>
    /// <param name="maximumBytes">The inclusive native frame ceiling, fully reserved by the caller before allocation.</param>
    /// <exception cref="KeyLoadException">The native message exceeds its byte or structural capacity.</exception>
    internal static void Validate(JsonRpcMessage message, int maximumBytes, IOptions<McpExecutionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var stream = new McpBoundedWriteStream(maximumBytes);
        try
        {
            JsonSerializer.Serialize(stream, message, McpJsonUtilities.DefaultOptions);
            _ = McpFrameBounds.InspectValue(stream.BorrowBuffer().Span[..stream.WrittenBytes], maximumBytes, options);
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, McpReplyProtocol.NativeOutputExceeded);
        }
    }
}
