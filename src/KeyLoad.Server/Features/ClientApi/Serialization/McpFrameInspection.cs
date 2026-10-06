using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Owns only active-object name sets while scanning a bounded wire buffer.</summary>
internal sealed class McpFrameInspection(bool boundIdentifier, IOptions<McpExecutionOptions> options)
{
    private readonly McpExecutionOptions settings = options.Value;
    private readonly HashSet<string>?[] names = new HashSet<string>?[options.Value.MaximumDepth];
    private int tokens;
    private int properties;
    private int maximumDepth;
    private bool rootIdentifier;

    internal McpFrameShape Shape => new(tokens, properties, maximumDepth);

    internal void Visit(ref Utf8JsonReader reader)
    {
        const int VisitPresentCount = 1;
        const int VisitAbsentCount = 0;

        var containerStart = reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray;
        var depth = reader.CurrentDepth + (containerStart ? VisitPresentCount : VisitAbsentCount);
        maximumDepth = Math.Max(maximumDepth, depth);
        if (++tokens > settings.MaximumTokens || depth > settings.MaximumDepth)
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
            && reader.ValueSpan.Length > settings.MaximumIdentifierBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded); }
        rootIdentifier = false;
    }

    private void AddProperty(ref Utf8JsonReader reader)
    {
        const int EmptyCurrentDepth = 1;
        const int NamesSecondIndex = 1;

        if (++properties > settings.MaximumProperties
            || reader.ValueSpan.Length > settings.MaximumPropertyNameBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded);
        }
        var name = McpFrameStrings.Decode(ref reader);
        rootIdentifier = reader.CurrentDepth == EmptyCurrentDepth
            && string.Equals(name, McpFramingProtocol.Identifier, StringComparison.Ordinal);
        if (!names[reader.CurrentDepth - NamesSecondIndex]!.Add(name))
        {
            throw Errors.Fail(ErrorCode.Validation, McpFramingProtocol.InvalidFrame);
        }
    }
}
