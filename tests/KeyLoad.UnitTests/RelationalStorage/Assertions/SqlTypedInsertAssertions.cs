using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlTypedInsertAssertions
{
    internal static Task SameAsync<TExpected, TActual>(TExpected expected, TActual actual)
        => SqlCreateTableAssertions.SameAsync(expected, actual);
    internal static Task UnchangedAsync(ZoneTreeStore store, string[] image, long cut)
        => SqlCreateTableAssertions.UnchangedAsync(store, image, cut);
    internal static byte[] OutcomeBytes(ZoneTreeStore store, PartitionRef partition, Guid id)
        => store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(partition, SqlTypedInsertLiteral.Writer, id))
            ?? throw new InvalidOperationException(MissingOutcome));
    internal static async Task ReceiptAsync(SqlTypedInsertExecution execution, params MutationReceipt[] expected)
    {
        var receipt = execution.Result.Get<CommitReceipt>();
        await SameAsync(new CommitReceipt(execution.Command.CommandId, execution.ExpectedToken,
            global::System.Collections.Immutable.ImmutableArray.CreateRange(expected), DurabilityProfile.ProcessDurable), receipt);
    }
    internal static async Task ImageAsync(TestDatabase fixture, ZoneTreeStore store, DatabaseEngine database,
        bool repaired, int coldRows = NoColdRows)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var cut = store.Position;
        await DocumentAsync(database, fixture.Partition, SqlTypedInsertLiteral.One, SqlTypedInsertLiteral.OneJson);
        await DocumentAsync(database, fixture.Partition, SqlTypedInsertLiteral.Two, SqlTypedInsertLiteral.TwoJson);
        if (repaired)
        {
            await DocumentAsync(database, fixture.Partition, SqlTypedInsertLiteral.Three, SqlTypedInsertLiteral.ThreeJson);
        }
        else
        {
            await Assert.That(database.GetDocument(SqlTypedInsertLiteral.Root, new(fixture.Partition, SqlTypedInsertLiteral.Table, SqlTypedInsertLiteral.Three))).IsNull();
        }
        var expected = new List<QueryRow>
        { new(SqlTypedInsertLiteral.One, InitialRevision, Alpha, false, []), new(SqlTypedInsertLiteral.Two, InitialRevision, Beta, false, []) };
        if (repaired)
        {
            expected.Insert(AfterOne, new(SqlTypedInsertLiteral.Three, InitialRevision, Gamma, false, []));
        }
        if (coldRows >= FirstCold)
        {
            await DocumentAsync(database, fixture.Partition, SqlTypedInsertCold.FirstId, SqlTypedInsertCold.FirstJson);
            expected.Add(new(SqlTypedInsertCold.FirstId, InitialRevision, ColdOne, false, []));
        }
        if (coldRows >= SecondCold)
        {
            await DocumentAsync(database, fixture.Partition, SqlTypedInsertCold.SecondId, SqlTypedInsertCold.SecondJson);
            expected.Add(new(SqlTypedInsertCold.SecondId, InitialRevision, ColdTwo, false, []));
        }
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var sql = engine.Execute(SqlTypedInsertLiteral.Root, new(fixture.Partition, SqlTypedInsertLiteral.Select, AllowFullScan: true));
        var ast = engine.ExecuteAst(SqlTypedInsertLiteral.Root, new(fixture.Partition,
            new SelectQuery(SqlTypedInsertLiteral.Table, null, [new(SqlTypedInsertLiteral.TitlePath, SqlTypedInsertLiteral.TitleColumn)],
                null, [new(IdPath, false)], MaximumRows), AllowFullScan: true));
        await PageAsync(sql, expected, cut);
        await PageAsync(ast, expected, cut);
        await UnchangedAsync(store, image, cut);
    }
    private static async Task DocumentAsync(DatabaseEngine database, PartitionRef partition, string id, string json)
        => await SameAsync(new DocumentResult(new(partition, SqlTypedInsertLiteral.Table, id), InitialRevision, json, false, []),
            database.GetDocument(SqlTypedInsertLiteral.Root, new(partition, SqlTypedInsertLiteral.Table, id)));
    private static async Task PageAsync(QueryPage page, List<QueryRow> expected, long cut)
    {
        await SameAsync(expected, page.Rows);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(FullScan);
        await Assert.That(page.CutPosition).IsEqualTo(cut);
    }
    private const string MissingOutcome = "The original typed INSERT outcome is not retained.";
    private const string Alpha = "{\"title\":\"alpha\"}";
    private const string Beta = "{\"title\":\"beta\"}";
    private const string Gamma = "{\"title\":\"gamma\"}";
    private const string ColdOne = "{\"title\":\"cold-one\"}";
    private const string ColdTwo = "{\"title\":\"cold-two\"}";
    private const string IdPath = "/@id";
    private const string FullScan = "bounded-full-scan";
    private const long InitialRevision = 1;
    private const int NoColdRows = 0;
    private const int FirstCold = 1;
    private const int SecondCold = 2;
    private const int AfterOne = 1;
    private const int MaximumRows = 8;
}
