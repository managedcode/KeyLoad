using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Performs resource framing checks before the official SDK creates its native DOM.</summary>
internal static class McpFrameBounds
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Checks one object frame and returns its actual bounded structure counts.</summary>
    /// <param name="payload">The private complete UTF-8 wire buffer.</param>
    /// <param name="maximumBytes">The inclusive wire-byte ceiling.</param>
    /// <param name="options">The centrally validated native MCP structural framing policy.</param>
    /// <returns>Counts used for retained-body admission.</returns>
    internal static McpFrameShape Inspect(ReadOnlySpan<byte> payload, int maximumBytes, IOptions<McpExecutionOptions> options)
        => InspectCore(payload, maximumBytes, options, requireObject: true);

    /// <summary>Checks one arbitrary canonical result value using the same framing ceilings.</summary>
    /// <param name="payload">The owned complete canonical UTF-8 result.</param>
    /// <param name="maximumBytes">The inclusive byte ceiling for this result lane.</param>
    /// <param name="options">The centrally validated native MCP structural framing policy.</param>
    /// <returns>Counts used before native result DOM allocation.</returns>
    internal static McpFrameShape InspectValue(ReadOnlySpan<byte> payload, int maximumBytes, IOptions<McpExecutionOptions> options)
        => InspectCore(payload, maximumBytes, options, requireObject: false);

    /// <summary>Checks a canonical result with room for the three native response wrappers.</summary>
    /// <param name="payload">The actual owned canonical result bytes.</param>
    /// <param name="maximumBytes">The inclusive canonical result ceiling.</param>
    /// <param name="options">The centrally validated native MCP reply and framing policy.</param>
    /// <returns>The bounded result shape before native output ownership is allocated.</returns>
    internal static McpFrameShape InspectReply(ReadOnlySpan<byte> payload, int maximumBytes, IOptions<McpExecutionOptions> options)
    {
        var shape = InspectValue(payload, maximumBytes, options);
        if (shape.Depth > options.Value.MaximumReplyDepth)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded); }
        return shape;
    }

    private static McpFrameShape InspectCore(ReadOnlySpan<byte> payload, int maximumBytes,
        IOptions<McpExecutionOptions> options, bool requireObject)
    {
        const int OtherSingleItemCount = 1;

        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, OtherSingleItemCount);
        if (payload.Length > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded);
        }
        try
        {
            _ = StrictUtf8.GetCharCount(payload);
            options.Value.Validate();
            return InspectJson(payload, requireObject, options);
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Validation, McpFramingProtocol.InvalidFrame);
        }
        catch (DecoderFallbackException)
        {
            throw Errors.Fail(ErrorCode.Validation, McpFramingProtocol.InvalidFrame);
        }
    }

    private static McpFrameShape InspectJson(ReadOnlySpan<byte> payload, bool requireObject, IOptions<McpExecutionOptions> options)
    {
        const int MaximumDepthStep = 1;

        var reader = new Utf8JsonReader(payload, new JsonReaderOptions
        {
            MaxDepth = options.Value.MaximumDepth + MaximumDepthStep,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        if (!reader.Read() || (requireObject && reader.TokenType != JsonTokenType.StartObject))
        {
            throw Errors.Fail(ErrorCode.Validation, McpFramingProtocol.InvalidFrame);
        }
        var inspection = new McpFrameInspection(requireObject, options);
        do
        { inspection.Visit(ref reader); }
        while (reader.Read());
        return inspection.Shape;
    }
}
