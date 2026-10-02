using System.Text;
using System.Text.Json;

namespace KeyLoad.Server;

/// <summary>Performs resource framing checks before the official SDK creates its native DOM.</summary>
internal static class McpFrameBounds
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Checks one object frame and returns its actual bounded structure counts.</summary>
    /// <param name="payload">The private complete UTF-8 wire buffer.</param>
    /// <param name="maximumBytes">The inclusive wire-byte ceiling.</param>
    /// <returns>Counts used for retained-body admission.</returns>
    internal static McpFrameShape Inspect(ReadOnlySpan<byte> payload, int maximumBytes)
        => InspectCore(payload, maximumBytes, requireObject: true);

    /// <summary>Checks one arbitrary canonical result value using the same framing ceilings.</summary>
    /// <param name="payload">The owned complete canonical UTF-8 result.</param>
    /// <param name="maximumBytes">The inclusive byte ceiling for this result lane.</param>
    /// <returns>Counts used before native result DOM allocation.</returns>
    internal static McpFrameShape InspectValue(ReadOnlySpan<byte> payload, int maximumBytes)
        => InspectCore(payload, maximumBytes, requireObject: false);

    /// <summary>Checks a canonical result with room for the three native response wrappers.</summary>
    /// <param name="payload">The actual owned canonical result bytes.</param>
    /// <param name="maximumBytes">The inclusive canonical result ceiling.</param>
    /// <returns>The bounded result shape before native output ownership is allocated.</returns>
    internal static McpFrameShape InspectReply(ReadOnlySpan<byte> payload, int maximumBytes)
    {
        var shape = InspectValue(payload, maximumBytes);
        if (shape.Depth > McpFramingProtocol.MaximumReplyDepth)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded); }
        return shape;
    }

    private static McpFrameShape InspectCore(ReadOnlySpan<byte> payload, int maximumBytes, bool requireObject)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        if (payload.Length > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded);
        }
        try
        {
            _ = StrictUtf8.GetCharCount(payload);
            return InspectJson(payload, requireObject);
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

    private static McpFrameShape InspectJson(ReadOnlySpan<byte> payload, bool requireObject)
    {
        var reader = new Utf8JsonReader(payload, new JsonReaderOptions
        {
            MaxDepth = McpFramingProtocol.MaximumDepth + 1,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        if (!reader.Read() || (requireObject && reader.TokenType != JsonTokenType.StartObject))
        {
            throw Errors.Fail(ErrorCode.Validation, McpFramingProtocol.InvalidFrame);
        }
        var inspection = new McpFrameInspection(requireObject);
        do
        { inspection.Visit(ref reader); }
        while (reader.Read());
        return inspection.Shape;
    }
}
