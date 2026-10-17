using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons.Targets;

internal sealed record OpenSearchDocumentScript(
    [property: JsonPropertyName(OpenSearchNames.ScriptSource)] string Source,
    [property: JsonPropertyName(OpenSearchNames.ScriptParameters)] OpenSearchDocumentParameters Parameters);
