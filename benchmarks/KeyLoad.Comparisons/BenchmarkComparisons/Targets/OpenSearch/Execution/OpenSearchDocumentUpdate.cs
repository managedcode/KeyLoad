using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons.Targets;

internal sealed record OpenSearchDocumentUpdate(
    [property: JsonPropertyName(OpenSearchNames.Script)] OpenSearchDocumentScript Script,
    [property: JsonPropertyName(OpenSearchNames.DocAsUpsert)] bool DocAsUpsert,
    [property: JsonPropertyName(OpenSearchNames.ScriptedUpsert)] bool ScriptedUpsert);
