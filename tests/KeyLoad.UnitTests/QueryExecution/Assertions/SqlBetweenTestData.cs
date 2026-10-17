using System.Text.Json;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlBetweenTestData
{
    internal const string Collection = "orders";
    internal const string IdPath = "/@id";
    internal const string NumberPath = "/number";
    internal const string LabelPath = "/label";
    internal const string FlagPath = "/flag";
    internal const string Root = "root";
    internal const string Separator = ",";
    internal const string RowLow = "low";
    internal const string RowMiddle = "middle";
    internal const string RowHigh = "high";
    internal const string RowNull = "null-number";
    internal const string RowMissing = "missing-number";
    internal const string RowLabel = "label-row";
    internal const string RowFlag = "flag-row";
    internal const string RowFieldBounds = "field-bounds";
    internal const string RowQuotedField = "quoted-field";
    internal const string ParameterLow = "lower";
    internal const string ParameterHigh = "upper";
    internal const string FieldBoundsDocument = "{\"number\":5,\"lowerBound\":1,\"upperBound\":9}";

    private const string LowDocument = "{\"number\":1,\"label\":\"amber\",\"flag\":false}";
    private const string MiddleDocument = "{\"number\":5,\"label\":\"moss\",\"flag\":true}";
    private const string HighDocument = "{\"number\":9,\"label\":\"violet\",\"flag\":false}";
    private const string NullDocument = "{\"number\":null}";
    private const string MissingDocument = "{}";
    private const string LabelDocument = "{\"label\":\"moss\"}";
    private const string FlagDocument = "{\"flag\":true}";

    internal static TestDatabase Create()
    {
        var database = new TestDatabase();
        try
        {
            database.Configure(Collection, ResourceKind.Collection);
            database.Commit(
                new PutDocument(Collection, RowLow, LowDocument),
                new PutDocument(Collection, RowMiddle, MiddleDocument),
                new PutDocument(Collection, RowHigh, HighDocument),
                new PutDocument(Collection, RowNull, NullDocument),
                new PutDocument(Collection, RowMissing, MissingDocument),
                new PutDocument(Collection, RowLabel, LabelDocument),
                new PutDocument(Collection, RowFlag, FlagDocument));
            return database;
        }
        catch (Exception)
        {
            database.Dispose();
            throw;
        }
    }

    internal static QueryRequest Request(TestDatabase database, string sql, Dictionary<string, JsonElement>? parameters = null)
        => new(database.Partition, sql, parameters, AllowFullScan: true);

    internal static Dictionary<string, JsonElement> Bounds(int lower, int upper)
        => new()
        {
            [ParameterLow] = JsonSerializer.SerializeToElement(lower),
            [ParameterHigh] = JsonSerializer.SerializeToElement(upper)
        };

    internal static string Ids(QueryPage page) => string.Join(Separator, page.Rows.Select(row => row.EntityId));

    internal static async Task SameRows(QueryPage expected, QueryPage actual)
    {
        await Assert.That(JsonDefaults.Serialize(actual.Rows).SequenceEqual(JsonDefaults.Serialize(expected.Rows))).IsTrue();
        await Assert.That(actual.Cursor is not null).IsEqualTo(expected.Cursor is not null);
        await Assert.That(actual.CutPosition).IsEqualTo(expected.CutPosition);
        await Assert.That(actual.AccessPath).IsEqualTo(expected.AccessPath);
    }
}
