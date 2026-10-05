using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal sealed class RelationalSchemaNameTests
{
    private const string Revision = "revision";
    private const string Wildcard = "*";
    private const string MetadataId = "@id";
    private const string MetadataPrefix = "@private";
    private const string Id = "id";
    private const string QuotedKey = "\"key\"";
    private const string QuotedId = "\"id\"";
    private const string QueryByPrimary = "SELECT d.id, d.name FROM \"typed-rows\" d WHERE d.id = 'first'";
    private const string PointAccess = "point";

    [Test]
    [Arguments(Revision)]
    [Arguments(Wildcard)]
    [Arguments(MetadataId)]
    [Arguments(MetadataPrefix)]
    [Arguments(Id)]
    public async Task AcAisql002ReservedMetadataNamesCannotShadowTypedColumns(string name)
    {
        using var database = new TestDatabase();
        var definition = RelationalTestData.Definition() with
        {
            RelationalSchema = new(RelationalTestData.Key, [.. RelationalTestData.Columns, new(name, RelationalColumnType.Text, Nullable: true)])
        };
        await Assert.That(RelationalTestData.ConfigureResult(database, definition).Error).IsEqualTo(ErrorCode.Validation);
        var missing = Assert.ThrowsExactly<KeyLoadException>(() => database.Store.Read(view =>
            database.Database.Resource(view, database.Partition, RelationalTestData.Table)));
        await Assert.That(missing.Code).IsEqualTo(ErrorCode.NotFound);
    }

    [Test]
    public async Task AcAisql003IdPrimaryKeyHasTheSameDocumentIdentityAndSqlPointResult()
    {
        using var database = new TestDatabase();
        var definition = RelationalTestData.Definition() with
        {
            RelationalSchema = new(Id, [.. RelationalTestData.Columns.Select(column =>
                column.Name == RelationalTestData.Key ? column with { Name = Id } : column)])
        };
        RelationalTestData.Configure(database, definition);
        database.Commit(new PutDocument(RelationalTestData.Table, RelationalTestData.First,
            RelationalTestData.Row().Replace(QuotedKey, QuotedId, StringComparison.Ordinal)));
        var document = database.Database.GetDocument(RelationalTestData.Root,
            new(database.Partition, RelationalTestData.Table, RelationalTestData.First))!;
        var query = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).Execute(RelationalTestData.Root, new(database.Partition, QueryByPrimary));
        await Assert.That(query.Rows).HasSingleItem();
        await Assert.That(query.AccessPath).IsEqualTo(PointAccess);
        await Assert.That(query.Rows[0].EntityId).IsEqualTo(document.Reference.Id);
        using var row = JsonDocument.Parse(query.Rows[0].Json);
        await Assert.That(row.RootElement.GetProperty(Id).GetString()).IsEqualTo(document.Reference.Id);
    }
}
