using System.Text.Json;

namespace KeyLoad.Server;

/// <summary>Owns only active-object name sets while scanning a bounded wire buffer.</summary>
internal sealed class McpFrameInspection(bool boundIdentifier)
{
    private readonly HashSet<string>?[] names = new HashSet<string>?[McpFramingProtocol.MaximumDepth];
    private int tokens;
    private int properties;
    private int maximumDepth;
    private bool rootIdentifier;

    internal McpFrameShape Shape => new(tokens, properties, maximumDepth);

    internal void Visit(ref Utf8JsonReader reader)
    {
        var containerStart = reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray;
        var depth = reader.CurrentDepth + (containerStart ? 1 : 0);
        maximumDepth = Math.Max(maximumDepth, depth);
        if (++tokens > McpFramingProtocol.MaximumTokens || depth > McpFramingProtocol.MaximumDepth)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded);
        }
        CheckIdentifier(ref reader);
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                names[reader.CurrentDepth] = new(StringComparer.Ordinal);
                break;
            case JsonTokenType.EndObject:
                names[reader.CurrentDepth] = null;
                break;
            case JsonTokenType.PropertyName:
                AddProperty(ref reader);
                break;
            case JsonTokenType.String when reader.ValueIsEscaped:
                _ = McpFrameStrings.Decode(ref reader);
                break;
        }
    }

    private void CheckIdentifier(ref Utf8JsonReader reader)
    {
        if (boundIdentifier && rootIdentifier && reader.TokenType == JsonTokenType.String
            && reader.ValueSpan.Length > McpFramingProtocol.MaximumIdentifierBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded); }
        rootIdentifier = false;
    }

    private void AddProperty(ref Utf8JsonReader reader)
    {
        if (++properties > McpFramingProtocol.MaximumProperties
            || reader.ValueSpan.Length > McpFramingProtocol.MaximumPropertyNameBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded);
        }
        var name = McpFrameStrings.Decode(ref reader);
        rootIdentifier = reader.CurrentDepth == 1
            && string.Equals(name, McpFramingProtocol.Identifier, StringComparison.Ordinal);
        if (!names[reader.CurrentDepth - 1]!.Add(name))
        {
            throw Errors.Fail(ErrorCode.Validation, McpFramingProtocol.InvalidFrame);
        }
    }
}
