using System.Text.Json;

namespace KeyLoad.Core;

internal static class EventVectorControlMetadataAction
{
    private const string ActionField = "action";
    private const string Invalid = "The native event vector control metadata action is inconsistent.";

    internal static EventVectorControlAction Read(string metadata)
    {
        using var document = JsonDocument.Parse(metadata);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty(ActionField, out var action)
            || action.ValueKind != JsonValueKind.String
            || !Enum.TryParse<EventVectorControlAction>(action.GetString(), out var value) || !Enum.IsDefined(value))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        return value;
    }
}
