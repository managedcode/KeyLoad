using System.Text.Json;

namespace KeyLoad.Server;

/// <summary>Writes complete bounded wrappers through the native writer without normalizing canonical result bytes.</summary>
internal static class McpReplyWriter
{
    /// <summary>Writes already checked canonical JSON without a parsed-value copy or number normalization.</summary>
    /// <param name="canonical">The value checked by the owning success factory.</param>
    /// <param name="requestId">The actual operation execution identity.</param>
    /// <param name="maximumBytes">The pre-reserved private wrapper capacity.</param>
    /// <returns>The complete independent wrapper bytes.</returns>
    internal static byte[] Success(ReadOnlySpan<byte> canonical, Guid requestId, int maximumBytes)
    {
        using var stream = new McpBoundedWriteStream(maximumBytes);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WritePropertyName(McpReplyProtocol.Result);
        writer.WriteRawValue(canonical, skipInputValidation: true);
        WriteRequestId(writer, requestId);
        writer.WriteEndObject();
        writer.Flush();
        return stream.ToOwnedArray();
    }

    /// <summary>Writes one newly created standard problem with a code-derived owned safe detail.</summary>
    /// <param name="code">The domain failure classification.</param>
    /// <param name="requestId">The real execution identity, absent before dispatch.</param>
    /// <param name="maximumBytes">The pre-reserved private wrapper capacity.</param>
    /// <returns>The complete independent wrapper bytes.</returns>
    internal static byte[] Failure(ErrorCode code, Guid? requestId, int maximumBytes)
    {
        using var stream = new McpBoundedWriteStream(maximumBytes);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WritePropertyName(McpReplyProtocol.Error);
        JsonSerializer.Serialize(writer, Errors.Problem(code, McpReplyProtocol.SafeDetail(code)), JsonDefaults.Options);
        WriteRequestId(writer, requestId);
        writer.WriteEndObject();
        writer.Flush();
        return stream.ToOwnedArray();
    }

    private static void WriteRequestId(Utf8JsonWriter writer, Guid? requestId)
    {
        if (requestId is { } id)
        { writer.WriteString(McpReplyProtocol.RequestId, id); }
        else
        { writer.WriteNull(McpReplyProtocol.RequestId); }
    }
}
