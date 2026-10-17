using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class OpenSearchDocument
{
    internal static OpenSearchDocumentSource Create(string id, string json, ImmutableArray<float> vector)
    {
        using var payload = JsonDocument.Parse(json);
        return new(id, payload.RootElement.Clone(), vector);
    }

    internal static OpenSearchDocumentSource CreateWithoutVector(string id, string json)
    {
        using var payload = JsonDocument.Parse(json);
        return new(id, payload.RootElement.Clone(), null);
    }

    internal static FoundDocument Read(JsonElement source)
    {
        var id = OpenSearchJson.RequiredString(source, OpenSearchNames.Id);
        var payload = OpenSearchJson.Required(source, OpenSearchNames.Payload, JsonValueKind.Object);
        return new(id, payload.GetRawText());
    }

    internal static OpenSearchDocumentUpdate CreateUpdate(string id, string json)
        => new(new(OpenSearchNames.ReplacementScript, new(CreateWithoutVector(id, json))), false, false);
}
