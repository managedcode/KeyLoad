using System.Text.Json;

namespace KeyLoad.Features.InternalSerialization;

internal static class NativeDomShape
{
    internal static void Validate(JsonTreeNode node)
    {
        var valid = node.Kind switch
        {
            JsonValueKind.Object => node.Properties is not null && node.Items is null && node.Text is null,
            JsonValueKind.Array => node.Items is not null && node.Properties is null && node.Text is null,
            JsonValueKind.String or JsonValueKind.Number => node.Text is not null && node.Items is null && node.Properties is null,
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => node.Text is null && node.Items is null && node.Properties is null,
            _ => false
        };
        if (!valid)
        {
            throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
        }
    }
}
