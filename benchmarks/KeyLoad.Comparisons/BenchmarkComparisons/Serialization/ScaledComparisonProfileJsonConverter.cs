using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

/// <summary>Serializes and strictly reconstitutes one of the exact closed scaled profile identities.</summary>
public sealed class ScaledComparisonProfileJsonConverter : JsonConverter<ScaledComparisonProfile>
{
    private const int PropertyCount = 14;
    private const string InvalidShape = "Scaled profile shape is invalid.";
    private const string SettingsMismatch = "Scaled profile settings mismatch.";
    private const string ConcurrencyProperty = "concurrency";
    private const string DimensionsProperty = "dimensions";
    private const string DocumentsProperty = "documents";
    private const string GraphDepthProperty = "graphDepth";
    private const string GraphFanOutProperty = "graphFanOut";
    private const string GraphVerticesProperty = "graphVertices";
    private const string IdProperty = "id";
    private const string OperationsProperty = "operations";
    private const string PayloadBytesProperty = "payloadBytes";
    private const string RepetitionsProperty = "repetitions";
    private const string SeedProperty = "seed";
    private const string TimeoutSecondsProperty = "timeoutSeconds";
    private const string TopKProperty = "topK";
    private const string WarmupProperty = "warmup";
    /// <inheritdoc />
    public override ScaledComparisonProfile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != PropertyCount
            || !root.TryGetProperty(IdProperty, out var id) || id.ValueKind != JsonValueKind.String)
        {
            throw new JsonException(InvalidShape);
        }
        var profile = ScaledComparisonProfileParser.Parse(id.GetString()!);
        Validate(root, profile);
        return profile;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ScaledComparisonProfile value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WriteString(IdProperty, value.Id);
        WriteSettings(writer, value);
        writer.WriteEndObject();
    }

    private static void Validate(JsonElement root, ScaledComparisonProfile profile)
    {
        Require(root, DocumentsProperty, profile.Documents);
        Require(root, OperationsProperty, profile.Operations);
        Require(root, WarmupProperty, profile.Warmup);
        Require(root, RepetitionsProperty, profile.Repetitions);
        Require(root, ConcurrencyProperty, profile.Concurrency);
        Require(root, PayloadBytesProperty, profile.PayloadBytes);
        Require(root, SeedProperty, profile.Seed);
        Require(root, DimensionsProperty, profile.Dimensions);
        Require(root, TopKProperty, profile.TopK);
        Require(root, TimeoutSecondsProperty, profile.TimeoutSeconds);
        Require(root, GraphVerticesProperty, profile.GraphVertices);
        Require(root, GraphFanOutProperty, profile.GraphFanOut);
        Require(root, GraphDepthProperty, profile.GraphDepth);
    }

    private static void Require(JsonElement root, string name, int expected)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var actual) || actual != expected)
        {
            throw new JsonException(SettingsMismatch);
        }
    }

    private static void WriteSettings(Utf8JsonWriter writer, ScaledComparisonProfile profile)
    {
        writer.WriteNumber(DocumentsProperty, profile.Documents);
        writer.WriteNumber(OperationsProperty, profile.Operations);
        writer.WriteNumber(WarmupProperty, profile.Warmup);
        writer.WriteNumber(RepetitionsProperty, profile.Repetitions);
        writer.WriteNumber(ConcurrencyProperty, profile.Concurrency);
        writer.WriteNumber(PayloadBytesProperty, profile.PayloadBytes);
        writer.WriteNumber(SeedProperty, profile.Seed);
        writer.WriteNumber(DimensionsProperty, profile.Dimensions);
        writer.WriteNumber(TopKProperty, profile.TopK);
        writer.WriteNumber(TimeoutSecondsProperty, profile.TimeoutSeconds);
        writer.WriteNumber(GraphVerticesProperty, profile.GraphVertices);
        writer.WriteNumber(GraphFanOutProperty, profile.GraphFanOut);
        writer.WriteNumber(GraphDepthProperty, profile.GraphDepth);
    }
}
