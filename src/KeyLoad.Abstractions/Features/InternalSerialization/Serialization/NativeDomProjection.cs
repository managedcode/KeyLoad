using System.Text.Json;

namespace KeyLoad.Features.InternalSerialization;

internal static class NativeDomProjection
{
    private const int RootDepth = 0;

    internal static JsonTreeNode Create(JsonElement value)
    {
        var root = Project(value, RootDepth);
        NativeDomPreflight.Validate(root);
        return root;
    }

    private static JsonTreeNode Project(JsonElement value, int depth)
    {
        var kind = value.ValueKind;
        if (kind is JsonValueKind.Object or JsonValueKind.Array)
        {
            if (++depth > NativeSerializationLimits.SemanticDepth)
            {
                throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
            }
        }
        return kind switch
        {
            JsonValueKind.Object => new() { Kind = kind, Properties = value.EnumerateObject().Select(property => ProjectProperty(property, depth)).ToArray() },
            JsonValueKind.Array => new() { Kind = kind, Items = value.EnumerateArray().Select(item => Project(item, depth)).ToArray() },
            JsonValueKind.String => new() { Kind = kind, Text = value.GetString()! },
            JsonValueKind.Number => new() { Kind = kind, Text = value.GetRawText() },
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => new() { Kind = kind },
            _ => throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload)
        };
    }

    private static JsonTreeProperty ProjectProperty(JsonProperty property, int depth)
        => new() { Name = property.Name, Value = Project(property.Value, depth) };
}
