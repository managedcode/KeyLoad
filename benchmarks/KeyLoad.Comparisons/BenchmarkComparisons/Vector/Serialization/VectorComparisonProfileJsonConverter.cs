using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

internal static class VectorProfileJsonFields
{
    internal const string Id = "id";
    internal const string RecordCount = "recordCount";
    internal const string IndexKind = "indexKind";
    internal const string QueryMode = "queryMode";
    internal const string Dimensions = "dimensions";
    internal const string Metric = "metric";
    internal const string TopK = "topK";
    internal const string Seed = "seed";
    internal const string PayloadBytes = "payloadBytes";
    internal const string QueryVectorCount = "queryVectorCount";
    internal const string WarmupQueries = "warmupQueries";
    internal const string MeasuredQueries = "measuredQueries";
    internal const string Concurrency = "concurrency";
    internal const string TimeoutSeconds = "timeoutSeconds";
    internal const string LatencySampleCount = "latencySampleCount";
    internal const string Repetitions = "repetitions";
    internal const string MinimumRecall = "minimumRecall";
    internal const string UpdateCount = "updateCount";
}

/// <summary>Persists the profile identity and rejects any caller-modified workload settings.</summary>
public sealed class VectorComparisonProfileJsonConverter : JsonConverter<VectorComparisonProfile>
{
    /// <inheritdoc />
    public override VectorComparisonProfile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var json = JsonDocument.ParseValue(ref reader);
        var root = json.RootElement;
        var profile = VectorComparisonProfile.Parse(root.GetProperty(VectorProfileJsonFields.Id).GetString()!);
        Require(root, VectorProfileJsonFields.RecordCount, profile.RecordCount);
        Require(root, VectorProfileJsonFields.IndexKind, profile.IndexKind.ToString());
        Require(root, VectorProfileJsonFields.QueryMode, profile.QueryMode.ToString());
        Require(root, VectorProfileJsonFields.Dimensions, profile.Dimensions);
        Require(root, VectorProfileJsonFields.Metric, profile.Metric);
        Require(root, VectorProfileJsonFields.TopK, profile.TopK);
        Require(root, VectorProfileJsonFields.Seed, profile.Seed);
        Require(root, VectorProfileJsonFields.PayloadBytes, profile.PayloadBytes);
        Require(root, VectorProfileJsonFields.QueryVectorCount, profile.QueryVectorCount);
        Require(root, VectorProfileJsonFields.WarmupQueries, profile.WarmupQueries);
        Require(root, VectorProfileJsonFields.MeasuredQueries, profile.MeasuredQueries);
        Require(root, VectorProfileJsonFields.Concurrency, profile.Concurrency);
        Require(root, VectorProfileJsonFields.TimeoutSeconds, profile.TimeoutSeconds);
        Require(root, VectorProfileJsonFields.LatencySampleCount, profile.LatencySampleCount);
        Require(root, VectorProfileJsonFields.Repetitions, profile.Repetitions);
        Require(root, VectorProfileJsonFields.MinimumRecall, profile.MinimumRecall);
        Require(root, VectorProfileJsonFields.UpdateCount, profile.UpdateCount);
        if (root.EnumerateObject().Count() != VectorComparisonProfileJsonConverterValues.ProfileFieldCount)
        {
            throw new JsonException(VectorComparisonProfileJsonConverterValues.VectorProfileHasUnexpectedFields);
        }
        return profile;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, VectorComparisonProfile value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WriteString(VectorProfileJsonFields.Id, value.Id);
        writer.WriteNumber(VectorProfileJsonFields.RecordCount, value.RecordCount);
        writer.WriteString(VectorProfileJsonFields.IndexKind, value.IndexKind.ToString());
        writer.WriteString(VectorProfileJsonFields.QueryMode, value.QueryMode.ToString());
        writer.WriteNumber(VectorProfileJsonFields.Dimensions, value.Dimensions);
        writer.WriteString(VectorProfileJsonFields.Metric, value.Metric);
        writer.WriteNumber(VectorProfileJsonFields.TopK, value.TopK);
        writer.WriteNumber(VectorProfileJsonFields.Seed, value.Seed);
        writer.WriteNumber(VectorProfileJsonFields.PayloadBytes, value.PayloadBytes);
        writer.WriteNumber(VectorProfileJsonFields.QueryVectorCount, value.QueryVectorCount);
        writer.WriteNumber(VectorProfileJsonFields.WarmupQueries, value.WarmupQueries);
        writer.WriteNumber(VectorProfileJsonFields.MeasuredQueries, value.MeasuredQueries);
        writer.WriteNumber(VectorProfileJsonFields.Concurrency, value.Concurrency);
        writer.WriteNumber(VectorProfileJsonFields.TimeoutSeconds, value.TimeoutSeconds);
        writer.WriteNumber(VectorProfileJsonFields.LatencySampleCount, value.LatencySampleCount);
        writer.WriteNumber(VectorProfileJsonFields.Repetitions, value.Repetitions);
        writer.WriteNumber(VectorProfileJsonFields.MinimumRecall, value.MinimumRecall);
        writer.WriteNumber(VectorProfileJsonFields.UpdateCount, value.UpdateCount);
        writer.WriteEndObject();
    }

    private static void Require(JsonElement root, string name, int value)
    {
        if (!root.TryGetProperty(name, out var actual) || actual.ValueKind != JsonValueKind.Number || actual.GetInt32() != value)
        {
            throw new JsonException($"{VectorComparisonProfileJsonConverterValues.VectorProfileField}{name}{VectorComparisonProfileJsonConverterValues.DiffersFromItsImmutableProfile}");
        }
    }

    private static void Require(JsonElement root, string name, double value)
    {
        if (!root.TryGetProperty(name, out var actual) || actual.ValueKind != JsonValueKind.Number || actual.GetDouble() != value)
        {
            throw new JsonException($"{VectorComparisonProfileJsonConverterValues.VectorProfileField}{name}{VectorComparisonProfileJsonConverterValues.DiffersFromItsImmutableProfile}");
        }
    }

    private static void Require(JsonElement root, string name, string value)
    {
        if (!root.TryGetProperty(name, out var actual) || actual.GetString() != value)
        {
            throw new JsonException($"{VectorComparisonProfileJsonConverterValues.VectorProfileField}{name}{VectorComparisonProfileJsonConverterValues.DiffersFromItsImmutableProfile}");
        }
    }
}
