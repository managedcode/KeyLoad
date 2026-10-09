using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlInnerJoinLifecycleTests
{
    private const string Left = "orders";
    private const string Right = "customers";
    private const string LeftPrimaryKeyColumn = "order_id";
    private const string LeftForeignKeyColumn = "customer_id";
    private const string RightPrimaryKeyColumn = "id";
    private const string GenerationField = "generation";
    private const string NameField = "name";
    private const string LeftEntityId = "a";
    private const string RightEntityId = "c1";
    private const int QueryDialectVersion = 2;
    private const int InitialGeneration = 1;
    private const int FirstCommittedGeneration = InitialGeneration + 1;
    private const int FinalGeneration = 33;
    private const int ReadOperationCount = 32;
    private const string Sql = "SELECT l.order_id AS order_id, r.name AS name, l.total AS generation "
        + "FROM orders AS l INNER JOIN customers AS r ON l.customer_id = r.id "
        + "ORDER BY l.order_id ASC LIMIT 10";

    [Test]
    public async Task AcJoinKeepsAtomicPairGenerationsConsistentAcrossConcurrentCommits()
    {
        using var database = new TestDatabase();
        Configure(database);
        database.Commit(new PutDocument(Left, "a", LeftRow(InitialGeneration)),
            new PutDocument(Right, "c1", RightRow(InitialGeneration)));
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readerWorkerCount = Math.Min(ReadOperationCount, database.Database.Limits.MaxConcurrentQueries);
        var pages = new QueryPage[ReadOperationCount];
        var writer = Task.Run(async () =>
        {
            await start.Task;
            for (var generation = FirstCommittedGeneration; generation <= FinalGeneration; generation++)
            {
                database.Commit(new PutDocument(Left, "a", LeftRow(generation)),
                    new PutDocument(Right, "c1", RightRow(generation)));
            }
        });
        var readers = Enumerable.Range(0, readerWorkerCount)
            .Select(worker => Task.Run(async () => await ReadAssignedPagesAsync(engine, database, start.Task, pages,
                worker, readerWorkerCount))).ToArray();
        start.SetResult(true);
        await Task.WhenAll(readers.Append(writer));

        foreach (var page in pages)
        {
            await AssertPairGenerationAsync(page);
        }
        var finalPage = engine.Execute("root", JoinRequest(database));
        await AssertPairGenerationAsync(finalPage, FinalGeneration);
    }

    private static async Task ReadAssignedPagesAsync(QueryEngine engine, TestDatabase database, Task start,
        QueryPage[] pages, int worker, int workerCount)
    {
        await start;
        for (var readIndex = worker; readIndex < pages.Length; readIndex += workerCount)
        {
            pages[readIndex] = engine.Execute("root", JoinRequest(database));
        }
    }

    private static QueryRequest JoinRequest(TestDatabase database)
        => new(database.Partition, Sql, AllowFullScan: true, QueryDialectVersion: QueryDialectVersion);

    private static async Task AssertPairGenerationAsync(QueryPage page, int? expectedGeneration = null)
    {
        await Assert.That(page.Rows).HasSingleItem();
        using var row = JsonDocument.Parse(page.Rows[0].Json);
        var generation = row.RootElement.GetProperty(GenerationField).GetInt32();
        var name = row.RootElement.GetProperty(NameField).GetString();
        await Assert.That(generation.ToString(System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(name);
        var sources = page.Rows[0].Sources!.Value;
        await Assert.That(sources[0].Revision).IsEqualTo(sources[1].Revision);
        if (expectedGeneration is int expected)
        {
            await Assert.That(generation).IsEqualTo(expected);
            await Assert.That(page.Rows[0].EntityId).IsEqualTo(LeftEntityId);
            await Assert.That(sources[0].EntityId).IsEqualTo(LeftEntityId);
            await Assert.That(sources[1].EntityId).IsEqualTo(RightEntityId);
            await Assert.That(sources[0].Revision).IsEqualTo(expected);
            await Assert.That(sources[1].Revision).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task AcJoinReopensCanonicalZoneTreeAndReturnsThePersistedPair()
    {
        using var database = new TestDatabase();
        Configure(database);
        var seed = database.Commit(new PutDocument(Left, "a", LeftRow(7)), new PutDocument(Right, "c1", RightRow(7)));
        database.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(database.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var owner = new DatabaseEngine(reopened, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(),
            UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(),
            UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(),
            UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);

        var page = new QueryEngine(owner, UnitExecutionOptions.QueryExecution())
            .Execute("root", JoinRequest(database));

        await Assert.That(page.Rows).HasSingleItem();
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(seed.Token.Position);
        using var row = JsonDocument.Parse(page.Rows[0].Json);
        await Assert.That(row.RootElement.GetProperty(GenerationField).GetInt32()).IsEqualTo(7);
        await Assert.That(row.RootElement.GetProperty(NameField).GetString()).IsEqualTo("7");
        var sources = page.Rows[0].Sources!.Value;
        await Assert.That(sources.Select(source => source.Revision).Distinct().Count()).IsEqualTo(1);
    }

    private static string LeftRow(int generation) => "{\"order_id\":\"a\",\"customer_id\":\"c1\",\"total\":"
        + generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";

    private static string RightRow(int generation) => "{\"id\":\"c1\",\"name\":\""
        + generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}";

    private static void Configure(TestDatabase database)
    {
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(Left, ResourceKind.Collection, database.Partition.TransactionDomainId)
            { RelationalSchema = new(LeftPrimaryKeyColumn, [new(LeftPrimaryKeyColumn, RelationalColumnType.Text), new(LeftForeignKeyColumn, RelationalColumnType.Text), new("total", RelationalColumnType.WholeNumber)]) }));
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(Right, ResourceKind.Collection, database.Partition.TransactionDomainId)
            { RelationalSchema = new(RightPrimaryKeyColumn, [new(RightPrimaryKeyColumn, RelationalColumnType.Text), new("name", RelationalColumnType.Text)]) }));
    }
}
