using System.Globalization;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class SurrealDbDocumentSql
{
    private const int MinimumNativeIdentifierLength = 2;
    private const int MaximumNativeIdentifierLength = 64;
    private const char IdentitySeparator = '-';
    private const string RecordIdentifierStart = "⟨";
    private const string RecordIdentifierEnd = "⟩";
    private const string NativeCREATEONLYSETKeyNumberPayloadEmbeddingFormatTemplate = "CREATE ONLY {0}:{1} SET key = {2}, number = {3}, payload = {4}, embedding = {5};";
    private static readonly System.Text.CompositeFormat NativeCREATEONLYSETKeyNumberPayloadEmbeddingFormat = System.Text.CompositeFormat.Parse(NativeCREATEONLYSETKeyNumberPayloadEmbeddingFormatTemplate);
    private const string NativeUPDATESETPayloadRETURNAFTERFormatTemplate = "UPDATE {0}:{1} SET payload = {2} RETURN AFTER;";
    private static readonly System.Text.CompositeFormat NativeUPDATESETPayloadRETURNAFTERFormat = System.Text.CompositeFormat.Parse(NativeUPDATESETPayloadRETURNAFTERFormatTemplate);
    private const string NativeDELETERETURNBEFOREFormatTemplate = "DELETE {0}:{1} RETURN BEFORE;";
    private static readonly System.Text.CompositeFormat NativeDELETERETURNBEFOREFormat = System.Text.CompositeFormat.Parse(NativeDELETERETURNBEFOREFormatTemplate);
    private const int SingleResultCardinality = 1;
    private const string RelationTraversal = "->";
    private const string GraphKeyProjection = ".key";
    private const char VectorComponentSeparator = ',';
    private const string NativeRETURNArraySortArrayDifferenceArrayDistinctArrayFormatTemplate = "RETURN array::sort(array::difference(array::distinct(array::flatten((SELECT VALUE [{0}] FROM {1}:{2})[0])), [{3}]));";
    private static readonly System.Text.CompositeFormat NativeRETURNArraySortArrayDifferenceArrayDistinctArrayFormat = System.Text.CompositeFormat.Parse(NativeRETURNArraySortArrayDifferenceArrayDistinctArrayFormatTemplate);
    internal static string Key(string value)
    {
        if (value.Length is < MinimumNativeIdentifierLength or > MaximumNativeIdentifierLength || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != IdentitySeparator))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return RecordIdentifierStart + value + RecordIdentifierEnd;
    }

    internal static string Create(string table, BenchmarkDocument document) => string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeCREATEONLYSETKeyNumberPayloadEmbeddingFormat, table, Key(document.Id), JsonSerializer.Serialize(document.Id), document.Number.ToString(CultureInfo.InvariantCulture), JsonSerializer.Serialize(document.Json), JsonSerializer.Serialize(document.Vector));
    internal static string Mutation(string table, Scenario scenario, BenchmarkDocument document) => scenario switch
    {
        Scenario.DocumentWrite => Create(table, document),
        Scenario.DocumentUpdate => string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeUPDATESETPayloadRETURNAFTERFormat, table, Key(document.Id), JsonSerializer.Serialize(document.Json)),
        Scenario.DocumentDelete => string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeDELETERETURNBEFOREFormat, table, Key(document.Id)),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario))
    };
    internal static string Graph(string table, string edge, string id, int depth)
    {
        var paths = Enumerable.Range(SingleResultCardinality, depth).Select(hops => string.Concat(Enumerable.Repeat(RelationTraversal + edge + RelationTraversal + table, hops)) + GraphKeyProjection);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeRETURNArraySortArrayDifferenceArrayDistinctArrayFormat, string.Join(VectorComponentSeparator, paths), table, Key(id), JsonSerializer.Serialize(id));
    }
}
