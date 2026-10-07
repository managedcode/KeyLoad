using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed record VectorAttachmentDocument([property: JsonPropertyName(VectorAttachmentDocument.ValueProperty)] string Value)
{
    internal const string ValueProperty = "value";
    internal const string VectorProperty = "embedding";

    [JsonPropertyName(VectorProperty)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<float> Embedding { get; init; }
}
