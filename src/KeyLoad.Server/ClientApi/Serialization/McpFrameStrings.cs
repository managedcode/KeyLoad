using System.Text.Json;

namespace KeyLoad.Server;

/// <summary>Classifies invalid escaped UTF-16 only at the native reader's string-decoding boundary.</summary>
internal static class McpFrameStrings
{
    /// <summary>Decodes a bounded native string token without reflecting its contents on failure.</summary>
    /// <param name="reader">The actual reader positioned on a property name or escaped string.</param>
    /// <returns>The decoded value, including valid surrogate pairs.</returns>
    internal static string Decode(ref Utf8JsonReader reader)
    {
        try
        {
            return reader.GetString() ?? throw Errors.Fail(ErrorCode.Validation, McpFramingProtocol.InvalidFrame);
        }
        catch (InvalidOperationException)
        {
            throw Errors.Fail(ErrorCode.Validation, McpFramingProtocol.InvalidFrame);
        }
    }
}
