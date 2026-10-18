using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons.Targets;

internal sealed record OpenSearchDocumentSource(string Id, JsonElement Payload,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<float>? Vector);
