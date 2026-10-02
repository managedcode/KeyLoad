using System.Collections.Immutable;
using KeyLoad.Core.Features.RelationalStorage;

namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal sealed class RelationalSchemaTests
{
    private const string MissingColumn = "missing";
    private const string NestedIndex = "/name/nested";
    private const string UnknownIndex = "/missing";
    private const string InvalidName = "bad\nname";
    private const int MaximumColumns = 256;

    [Test]
    public async Task AcAisql002SchemaPersistsAndChangingItsContractRequiresMigration()
    {
        using var database = new TestDatabase();
        var definition = RelationalTestData.Definition();
        var configured = RelationalTestData.Configure(database, definition);
        await Assert.That(JsonDefaults.Serialize(configured).SequenceEqual(JsonDefaults.Serialize(definition))).IsTrue();
        var persisted = database.Store.Read(view => database.Database.Resource(view, database.Partition, RelationalTestData.Table));
        await Assert.That(persisted.RelationalSchema!.Columns.Length).IsEqualTo(RelationalTestData.Columns.Length);
        var changed = definition with { RelationalSchema = new(RelationalTestData.Key, [new(RelationalTestData.Key, RelationalColumnType.Text)]) };
        await Assert.That(RelationalTestData.ConfigureResult(database, changed).Error).IsEqualTo(ErrorCode.UnsupportedCapability);
    }

    [Test]
    public async Task AcAisql002InvalidSchemasCannotBecomeCatalogResources()
    {
        using var database = new TestDatabase();
        var definition = RelationalTestData.Definition();
        ResourceDefinition[] invalid =
        [
            definition with { RelationalSchema = new(RelationalTestData.Key, []) },
            definition with { RelationalSchema = new(MissingColumn, RelationalTestData.Columns) },
            definition with { RelationalSchema = new(RelationalTestData.Key, [new(RelationalTestData.Key, RelationalColumnType.Text, true)]) },
            definition with { RelationalSchema = new(RelationalTestData.Key, [new(RelationalTestData.Key, RelationalColumnType.WholeNumber)]) },
            definition with { RelationalSchema = new(RelationalTestData.Key, [.. RelationalTestData.Columns, RelationalTestData.Columns[0]]) },
            definition with { RelationalSchema = new(RelationalTestData.Key, [new(RelationalTestData.Key, (RelationalColumnType)int.MaxValue)]) },
            definition with { RelationalSchema = new(InvalidName, [new(InvalidName, RelationalColumnType.Text)]) },
            definition with { Indexes = [new(RelationalTestData.IndexName, [NestedIndex])] },
            definition with { Indexes = [new(RelationalTestData.IndexName, [UnknownIndex])] },
            definition with { Kind = ResourceKind.StreamSet },
            definition with { Authority = DocumentAuthority.EventStream }
        ];
        foreach (var candidate in invalid)
        {
            await Assert.That(RelationalTestData.ConfigureResult(database, candidate).Error).IsEqualTo(ErrorCode.Validation);
        }
        var missing = Assert.ThrowsExactly<KeyLoadException>(() => database.Store.Read(view =>
            database.Database.Resource(view, database.Partition, RelationalTestData.Table)));
        await Assert.That(missing.Code).IsEqualTo(ErrorCode.NotFound);
    }

    [Test]
    public async Task AcAisql002ColumnBoundsAndMalformedInProcessShapesRejectBeforeSerialization()
    {
        var definition = RelationalTestData.Definition();
        var defaultColumns = definition with { RelationalSchema = new(RelationalTestData.Key, default) };
        var nullColumn = definition with { RelationalSchema = new(RelationalTestData.Key, [null!]) };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => RelationalRowValidation.ValidateSchema(defaultColumns)).Code)
            .IsEqualTo(ErrorCode.Validation);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => RelationalRowValidation.ValidateSchema(nullColumn)).Code)
            .IsEqualTo(ErrorCode.Validation);
        var columns = Enumerable.Range(0, MaximumColumns).Select(index => new RelationalColumn(
            RelationalTestData.Name + index, RelationalColumnType.Text, Nullable: true)).Prepend(new(RelationalTestData.Key, RelationalColumnType.Text))
            .ToImmutableArray();
        using var database = new TestDatabase();
        var atLimit = definition with { Indexes = [], RelationalSchema = new(RelationalTestData.Key, columns[..MaximumColumns]) };
        RelationalTestData.Configure(database, atLimit);
        var overLimit = definition with { RelationalSchema = new(RelationalTestData.Key, columns) };
        await Assert.That(RelationalTestData.ConfigureResult(database, overLimit).Error).IsEqualTo(ErrorCode.ResourceExhausted);
    }
}
