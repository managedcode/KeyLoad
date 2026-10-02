using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons.Targets;

internal sealed record OpenSearchDocumentSource(string Id, JsonElement Payload,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<float>? Vector);

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
}
