using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Owns SurrealDB HTTP framing and its native response-envelope validation.</summary>
internal static class SurrealDbVectorProtocol
{
    private const int SingleResultCardinality = 1;
    private const int EmptyResultCount = 0;
    private const char RecordSeparator = ':';
    private const int NativeConstant12000 = 12_000;
    private const string CREATEONLY = "CREATE ONLY ";
    private const string SETNumber = " SET number = ";
    private const string Embedding = ", embedding = ";
    private const string Payload = ", payload = ";
    private const char NativeCharacter59 = ';';
    private const string NativeSELECTNumberIdEmbeddingPayloadFROMWHERENumberFormatTemplate = "SELECT number, id, embedding, payload FROM {0} WHERE number > {1} ORDER BY number LIMIT {2};";
    private static readonly System.Text.CompositeFormat NativeSELECTNumberIdEmbeddingPayloadFROMWHERENumberFormat = System.Text.CompositeFormat.Parse(NativeSELECTNumberIdEmbeddingPayloadFROMWHERENumberFormatTemplate);
    private const string NativeSELECTNumberIdEmbeddingPayloadFROMFormatTemplate = "SELECT number, id, embedding, payload FROM {0}:{1};";
    private static readonly System.Text.CompositeFormat NativeSELECTNumberIdEmbeddingPayloadFROMFormat = System.Text.CompositeFormat.Parse(NativeSELECTNumberIdEmbeddingPayloadFROMFormatTemplate);
    private const string NativeUPDATESETEmbeddingFormatTemplate = "UPDATE {0}:{1} SET embedding = {2};";
    private static readonly System.Text.CompositeFormat NativeUPDATESETEmbeddingFormat = System.Text.CompositeFormat.Parse(NativeUPDATESETEmbeddingFormatTemplate);
    private const string NativeEmptyTextFormatTemplate = "{0}, {1}";
    private static readonly System.Text.CompositeFormat NativeEmptyTextFormat = System.Text.CompositeFormat.Parse(NativeEmptyTextFormatTemplate);
    private const string NativeCOSINEFormatTemplate = "{0}, COSINE";
    private static readonly System.Text.CompositeFormat NativeCOSINEFormat = System.Text.CompositeFormat.Parse(NativeCOSINEFormatTemplate);
    private const string NativeSELECTIdVectorDistanceKnnASDistanceFROMFormatTemplate = "SELECT id, vector::distance::knn() AS distance FROM {0} WHERE embedding <|{1}|> {2}{3} ORDER BY distance, id LIMIT {4};";
    private static readonly System.Text.CompositeFormat NativeSELECTIdVectorDistanceKnnASDistanceFROMFormat = System.Text.CompositeFormat.Parse(NativeSELECTIdVectorDistanceKnnASDistanceFROMFormatTemplate);
    private const string EXPLAIN = "EXPLAIN FORMAT JSON ";
    private const int CanonicalTopK = 10;
    private const string NativeDEFINEINDEXONTABLEFIELDSEmbeddingHNSWDIMENSIONFormatTemplate = "DEFINE INDEX {0} ON TABLE {1} FIELDS embedding HNSW DIMENSION {2} TYPE F32 DIST COSINE EFC {3} M {4} CONCURRENTLY;";
    private static readonly System.Text.CompositeFormat NativeDEFINEINDEXONTABLEFIELDSEmbeddingHNSWDIMENSIONFormat = System.Text.CompositeFormat.Parse(NativeDEFINEINDEXONTABLEFIELDSEmbeddingHNSWDIMENSIONFormatTemplate);
    private const string ANDNumber = " AND number % 100 = 0";
    private const string ANDNumber2 = " AND number % 10 != 9";
    private const int VectorComponentTextCapacity = 12;
    private const int VectorArrayDelimiterWidth = 2;
    private const char VectorArrayStart = '[';
    private const char VectorComponentSeparator = ',';
    private const char VectorArrayEnd = ']';
    private const char VectorRecordPrefix = 'v';
    private const char NativeCharacter48 = '0';
    private const char NativeCharacter57 = '9';
    private const string VectorValueFormat = "R";
    private const string InvalidResponse = "SurrealDbInvalidResponse";
    private const string ResultKey = "result";
    internal static JsonElement SingleResult(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != SingleResultCardinality)
        {
            throw new InvalidDataException(InvalidResponse);
        }

        return root[EmptyResultCount].GetProperty(ResultKey);
    }

    internal static string ReadRequiredString(JsonElement element, string name) => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString()! : throw new InvalidDataException(InvalidResponse);
    internal static double ReadRequiredDouble(JsonElement element, string name) => element.TryGetProperty(name, out var property) && property.TryGetDouble(out var value) && double.IsFinite(value) ? value : throw new InvalidDataException(InvalidResponse);
    internal static float[] ReadVector(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(InvalidResponse);
        }

        var vector = new float[value.GetArrayLength()];
        var index = EmptyResultCount;
        foreach (var component in value.EnumerateArray())
        {
            var number = component.GetSingle();
            if (!float.IsFinite(number))
            {
                throw new InvalidDataException(InvalidResponse);
            }

            vector[index++] = number;
        }

        return vector;
    }

    internal static string ReadRecordId(string table, JsonElement id)
    {
        if (id.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(InvalidResponse);
        }

        var value = id.GetString()!;
        var separator = value.LastIndexOf(RecordSeparator);
        if (separator < EmptyResultCount || !value.AsSpan(EmptyResultCount, separator).SequenceEqual(table))
        {
            throw new InvalidDataException(InvalidResponse);
        }

        return value[(separator + SingleResultCardinality)..];
    }

    internal static string CreateBatchSql(string table, List<VectorDocument> batch)
    {
        var sql = new StringBuilder(batch.Count * NativeConstant12000);
        foreach (var document in batch)
        {
            sql.Append(CREATEONLY).Append(table).Append(RecordSeparator).Append(RecordKey(document.Id)).Append(SETNumber).Append(document.Number.ToString(CultureInfo.InvariantCulture)).Append(Embedding).Append(FormatVector(document.Embedding.Span)).Append(Payload).Append(JsonSerializer.Serialize(document.Payload)).Append(NativeCharacter59);
        }

        return sql.ToString();
    }

    internal static string ReadbackSql(string table, int after, int limit) => string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeSELECTNumberIdEmbeddingPayloadFROMWHERENumberFormat, table, after, limit);
    internal static string ReadOneSql(string table, string id) => string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeSELECTNumberIdEmbeddingPayloadFROMFormat, table, RecordKey(id));
    internal static string UpdateSql(string table, string id, ReadOnlySpan<float> vector) => string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeUPDATESETEmbeddingFormat, table, RecordKey(id), FormatVector(vector));
    internal static string SearchSql(string table, ReadOnlySpan<float> vector, int topK, VectorQueryMode mode, VectorIndexKind index, int ef)
    {
        var operatorParameters = index == VectorIndexKind.Hnsw ? string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeEmptyTextFormat, topK, ef) : string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeCOSINEFormat, topK);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeSELECTIdVectorDistanceKnnASDistanceFROMFormat, table, operatorParameters, FormatVector(vector), FilterClause(mode), topK);
    }

    internal static string ExplainSql(string table, ReadOnlySpan<float> vector, VectorQueryMode mode, VectorIndexKind index, int ef) => EXPLAIN + SearchSql(table, vector, CanonicalTopK, mode, index, ef);
    internal static string HnswIndex(string index, string table, int dimensions, int efConstruction, int neighbors) => string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeDEFINEINDEXONTABLEFIELDSEmbeddingHNSWDIMENSIONFormat, index, table, dimensions, efConstruction, neighbors);
    private static string FilterClause(VectorQueryMode mode) => mode switch
    {
        VectorQueryMode.Plain => string.Empty,
        VectorQueryMode.Filtered => ANDNumber,
        VectorQueryMode.Mixed => ANDNumber2,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))};
    private static string FormatVector(ReadOnlySpan<float> vector)
    {
        var result = new StringBuilder(vector.Length * VectorComponentTextCapacity + VectorArrayDelimiterWidth).Append(VectorArrayStart);
        for (var index = EmptyResultCount; index < vector.Length; index++)
        {
            if (!float.IsFinite(vector[index]))
            {
                throw new InvalidDataException(SurrealDbNativeTokens.TokenSurrealDbInvalidVector);
            }

            if (index != EmptyResultCount)
            {
                result.Append(VectorComponentSeparator);
            }

            result.Append(vector[index].ToString(VectorValueFormat, CultureInfo.InvariantCulture));
        }

        return result.Append(VectorArrayEnd).ToString();
    }

    private static string RecordKey(string id) => id.Length == CanonicalTopK && id[EmptyResultCount] == VectorRecordPrefix && id.AsSpan(SingleResultCardinality).IndexOfAnyExceptInRange(NativeCharacter48, NativeCharacter57) < EmptyResultCount ? SurrealDbNativeTokens.TokenV + id[SingleResultCardinality..] : throw new InvalidDataException(SurrealDbNativeTokens.TokenSurrealDbInvalidRecordId);
}
