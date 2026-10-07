using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class KeyLoadQueryFactoryTests
{
    private const string CollectionName = "factory-records";
    private const string StatusIndex = "status";
    private const string StatusPath = "/status";
    private const string OpenStatus = "open";
    private const string ClosedStatus = "closed";
    private const string RootPrincipal = "root";
    private const int MinimumPriority = 5;
    private const int HighestPriority = 9;
    private const int TiedPriority = 7;
    private const int PriorityStep = 1;
    private const int PageSize = 2;
    private const long InitialRevision = 1;
    private const string FirstId = "order-zeta";
    private const string SecondId = "order-alpha";
    private const string ThirdId = "order-beta";
    private const string FourthId = "order-gamma";
    private const string ClosedId = "order-closed";
    private const string BelowMinimumId = "order-below";

    private sealed record FactoryRecord(string Name, int Priority, string Status);

    private sealed record FactoryProjection(string Id, long Revision, string Name, int Priority);

    [Test]
    public async Task AcQueryFactory001TypedBuilderExecutesAgainstNativeZoneTreeRows()
    {
        using var database = new TestDatabase();
        var readCut = Seed(database);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());

        var request = CreateQuery(database.Partition);
        await Assert.That(request.Partition).IsEqualTo(database.Partition);
        var page = engine.ExecuteAst(RootPrincipal, request);

        await AssertExpectedRows(database, page, readCut);
    }

    [Test]
    public async Task AcQueryFactory002NullArgumentsPreserveNativeStoreAndHealthyQuery()
    {
        using var database = new TestDatabase();
        var readCut = Seed(database);
        var translation = UnitClientOptions.Translation();
        var partitionFailure = Assert.ThrowsExactly<ArgumentNullException>(() =>
            KeyLoadQuery.From<FactoryRecord>(null!, CollectionName, translation));
        var collectionFailure = Assert.ThrowsExactly<ArgumentNullException>(() =>
            KeyLoadQuery.From<FactoryRecord>(database.Partition, null!, translation));
        var initial = KeyLoadQuery.From<FactoryRecord>(database.Partition, CollectionName, translation);
        var whereFailure = Assert.ThrowsExactly<ArgumentNullException>(() => initial.Where(null!));
        var selectFailure = Assert.ThrowsExactly<ArgumentNullException>(() => initial.Select<FactoryProjection>(null!));

        await Assert.That(partitionFailure.ParamName).IsEqualTo("partition");
        await Assert.That(collectionFailure.ParamName).IsEqualTo("collection");
        await Assert.That(whereFailure.ParamName).IsEqualTo("expression");
        await Assert.That(selectFailure.ParamName).IsEqualTo("expression");
        await Assert.That(database.Store.Position).IsEqualTo(readCut);

        var request = CreateQuery(database.Partition);
        await Assert.That(request.Partition).IsEqualTo(database.Partition);
        var page = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution())
            .ExecuteAst(RootPrincipal, request);

        await AssertExpectedRows(database, page, readCut);
    }

    private static long Seed(TestDatabase database)
    {
        database.Configure(CollectionName, ResourceKind.Collection,
            indexes: [new(StatusIndex, [StatusPath])]);
        database.Commit(
            Put(FirstId, "Zeta", HighestPriority, OpenStatus),
            Put(SecondId, "Alpha", TiedPriority, OpenStatus),
            Put(ThirdId, "Beta", TiedPriority, OpenStatus),
            Put(FourthId, "Gamma", MinimumPriority, OpenStatus),
            Put(ClosedId, "Closed", PageSize + MinimumPriority, ClosedStatus),
            Put(BelowMinimumId, "Below", MinimumPriority - PriorityStep, OpenStatus));
        return database.Store.Position;
    }

    private static PutDocument Put(string id, string name, int priority, string status)
        => new(CollectionName, id, JsonSerializer.Serialize(new FactoryRecord(name, priority, status), JsonDefaults.Options));

    private static AstQueryRequest CreateQuery(PartitionRef partition)
        => KeyLoadQuery.From<FactoryRecord>(partition, CollectionName, UnitClientOptions.Translation())
            .Where(record => record.Status == OpenStatus && record.Priority >= MinimumPriority)
            .OrderByDescending(record => record.Priority)
            .ThenBy(record => QueryFunctions.DocumentId(record))
            .Select(record => new
            {
                Id = QueryFunctions.DocumentId(record),
                Revision = QueryFunctions.DocumentRevision(record),
                record.Name,
                record.Priority
            })
            .Take(PageSize)
            .ToRequest();

    private static async Task AssertExpectedRows(TestDatabase database, QueryPage page, long readCut)
    {
        var expected = new[]
        {
            new FactoryProjection(FirstId, InitialRevision, "Zeta", HighestPriority),
            new FactoryProjection(SecondId, InitialRevision, "Alpha", TiedPriority)
        };
        await Assert.That(page.Rows.Select(row => row.EntityId).SequenceEqual(expected.Select(row => row.Id))).IsTrue();
        await Assert.That(page.Rows.Select(row => row.Revision).SequenceEqual(expected.Select(row => row.Revision))).IsTrue();
        await Assert.That(page.Rows.Select(Project).SequenceEqual(expected)).IsTrue();
        await Assert.That(page.CutPosition).IsEqualTo(readCut);
        await Assert.That(database.Store.Position).IsEqualTo(readCut);
    }

    private static FactoryProjection Project(QueryRow row) => JsonDefaults.Deserialize<FactoryProjection>(row.Json);
}
