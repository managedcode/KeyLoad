using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons.Targets;

internal sealed record OpenSearchDocumentParameters(
    [property: JsonPropertyName(OpenSearchNames.ScriptSource)] OpenSearchDocumentSource Source);
