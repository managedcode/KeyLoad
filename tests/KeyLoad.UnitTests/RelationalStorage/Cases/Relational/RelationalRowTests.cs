using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal sealed class RelationalRowTests
{
    private const string PropertyFormat = "\"{0}\":{1}";
    private const string MissingName = "\"name\":\"alpha\",";
    private const string NullName = "null";
    private const string NumericText = "42";
    private const string QuotedNumber = "\"1\"";
    private const string FractionalInteger = "1.0";
    private const string OverflowInteger = "9223372036854775808";
    private const string OverPrecisionDecimal = "1.00000000000000000000000000001";
    private const string DecimalUnderflow = "1e-29";
    private const string Offsetless = "\"2026-10-02T12:00:00\"";
    private const string NonUtc = "\"2026-10-02T12:00:00+01:00\"";
    private const string InvalidDate = "\"2026-02-30T12:00:00Z\"";
    private const string UnknownProperty = "{\"unknown\":1,";
    private const string DuplicateProperty = "{\"name\":\"alpha\",";
    private const string NullableProperty = ",\"note\":null}";
    private const string ChangedKey = "\"other\"";
    private const string InvalidArray = "[]";
    private const string ValidDecimalExponent = "12500e-4";
    private const string UtcOffset = "\"2026-10-02T12:00:00+00:00\"";
    private const string Int64Minimum = "-9223372036854775808";
    private const string Int64Maximum = "9223372036854775807";
    private const string DecimalMaximum = "79228162514264337593543950335";
    private const string DecimalMinimumQuantum = "0.0000000000000000000000000001";
    private const string ValidTrailingZeros = "1.25000000000000000000000000000";
    private static readonly CompositeFormat ParsedPropertyFormat = CompositeFormat.Parse(PropertyFormat);

    [Test]
    public async Task AcAisql003ValidScalarLimitsUtcAndNullableAbsenceCommitWithoutCoercion()
    {
        using var database = new TestDatabase();
        RelationalTestData.Configure(database);
        var json = Replace(RelationalTestData.Row(), RelationalTestData.Count, Int64Minimum);
        json = Replace(json, RelationalTestData.Amount, DecimalMaximum);
        json = Replace(json, RelationalTestData.Timestamp, UtcOffset);
        json = json[..^1] + NullableProperty;
        database.Commit(new PutDocument(RelationalTestData.Table, RelationalTestData.First, json));
        using var stored = JsonDocument.Parse(database.Database.GetDocument(RelationalTestData.Root,
            new(database.Partition, RelationalTestData.Table, RelationalTestData.First))!.Json);
        await Assert.That(stored.RootElement.GetProperty(RelationalTestData.Count).GetInt64()).IsEqualTo(long.MinValue);
        await Assert.That(stored.RootElement.GetProperty(RelationalTestData.Amount).GetDecimal()).IsEqualTo(decimal.MaxValue);
        await Assert.That(stored.RootElement.GetProperty(RelationalTestData.Note).ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task AcAisql003ClosedRequiredTypedRowsRejectAndLeaveNoDocumentOrOutbox()
    {
        using var database = new TestDatabase();
        RelationalTestData.Configure(database);
        var row = RelationalTestData.Row();
        string[] invalid =
        [
            row.Replace(MissingName, string.Empty, StringComparison.Ordinal), Replace(row, RelationalTestData.Name, NullName),
            Replace(row, RelationalTestData.Name, NumericText), Replace(row, RelationalTestData.Count, QuotedNumber),
            Replace(row, RelationalTestData.Count, FractionalInteger), Replace(row, RelationalTestData.Count, OverflowInteger),
            Replace(row, RelationalTestData.Active, NumericText), Replace(row, RelationalTestData.Amount, OverPrecisionDecimal),
            Replace(row, RelationalTestData.Amount, DecimalUnderflow), Replace(row, RelationalTestData.Timestamp, Offsetless),
            Replace(row, RelationalTestData.Timestamp, NonUtc), Replace(row, RelationalTestData.Timestamp, InvalidDate),
            Replace(row, RelationalTestData.Key, ChangedKey), UnknownProperty + row[1..], DuplicateProperty + row[1..], InvalidArray
        ];
        foreach (var json in invalid)
        {
            await Assert.That(RelationalTestData.Submit(database, new PutDocument(RelationalTestData.Table, RelationalTestData.First, json)).Error)
                .IsEqualTo(ErrorCode.Validation);
        }
        await Assert.That(database.Database.GetDocument(RelationalTestData.Root,
            new(database.Partition, RelationalTestData.Table, RelationalTestData.First))).IsNull();
        await Assert.That(database.Database.GetOutboxStatus(RelationalTestData.Root, database.Partition).Head.Tail).IsEqualTo(0);
    }

    [Test]
    public async Task AcAisql003PatchValidatesFinalImageAndRawDecimalBeforeCanonicalRounding()
    {
        using var database = new TestDatabase();
        RelationalTestData.Configure(database);
        database.Commit(new PutDocument(RelationalTestData.Table, RelationalTestData.First, RelationalTestData.Row()));
        FieldPatch[] invalid =
        [
            new(RelationalTestData.NamePath, PatchKind.Remove), new(RelationalTestData.CountPath, PatchKind.Set, QuotedNumber),
            new(RelationalTestData.AmountPath, PatchKind.Set, OverPrecisionDecimal), new(RelationalTestData.KeyPath, PatchKind.Set, ChangedKey)
        ];
        foreach (var patch in invalid)
        {
            await Assert.That(RelationalTestData.Submit(database, new PatchDocument(RelationalTestData.Table, RelationalTestData.First, [patch], 1)).Error)
                .IsEqualTo(ErrorCode.Validation);
        }
        database.Commit(new PatchDocument(RelationalTestData.Table, RelationalTestData.First,
            [new(RelationalTestData.AmountPath, PatchKind.Set, ValidDecimalExponent)], 1));
        var stored = database.Database.GetDocument(RelationalTestData.Root,
            new(database.Partition, RelationalTestData.Table, RelationalTestData.First))!;
        await Assert.That(stored.Revision).IsEqualTo(2);
        using var document = JsonDocument.Parse(stored.Json);
        await Assert.That(document.RootElement.GetProperty(RelationalTestData.Amount).GetDecimal()).IsEqualTo(1.25m);
        await Assert.That(database.Database.GetOutboxStatus(RelationalTestData.Root, database.Partition).Head.Tail).IsEqualTo(2);
    }

    [Test]
    [Arguments(Int64Maximum, DecimalMinimumQuantum)]
    [Arguments(Int64Minimum, ValidTrailingZeros)]
    public async Task AcAisql003ExactNumericBoundarySpellingsRemainAccepted(string integer, string number)
    {
        using var database = new TestDatabase();
        RelationalTestData.Configure(database);
        var json = Replace(Replace(RelationalTestData.Row(), RelationalTestData.Count, integer), RelationalTestData.Amount, number);
        await Assert.That(RelationalTestData.Submit(database, new PutDocument(RelationalTestData.Table, RelationalTestData.First, json)).Error).IsNull();
    }

    private static string Replace(string row, string column, string value)
    {
        using var document = JsonDocument.Parse(row);
        var before = string.Format(CultureInfo.InvariantCulture, ParsedPropertyFormat, column, document.RootElement.GetProperty(column).GetRawText());
        var after = string.Format(CultureInfo.InvariantCulture, ParsedPropertyFormat, column, value);
        return row.Replace(before, after, StringComparison.Ordinal);
    }
}
