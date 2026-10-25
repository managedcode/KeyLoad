using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.UnitTests.Authorization;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class SqlBetweenBudgetAndAuthorityTests
{
    private const string RangeSql = "SELECT * FROM orders WHERE number BETWEEN 1 AND 9";
    private const string AuthorizedFieldBoundsSql = "SELECT * FROM orders WHERE number BETWEEN lowerBound AND upperBound";
    private const string Reader = "reader";
    private const string Tenant = "tenant";
    private const string Database = "database";
    private const string Sensitive = "pii";

    [Test]
    public async Task AcSqlc006ABothBoundFieldsAreAuthorizedBeforeRangeExecution()
    {
        var failures = new List<Exception>();
        try
        {
            using var database = new TestDatabase();
            try
            {
                database.Configure(SqlBetweenTestData.Collection, ResourceKind.Collection,
                    fields: [new("/lowerBound", Sensitive), new("/upperBound", Sensitive)]);
                database.Commit(new PutDocument(SqlBetweenTestData.Collection, SqlBetweenTestData.RowMiddle,
                    "{\"number\":5,\"lowerBound\":1,\"upperBound\":9}"));
                ConfigureReader(database);
                var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
                var image = TeamRowPolicyAssertions.Image(database.Store);
                var position = database.Store.Position;
                var lowerDenied = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(Reader,
                    SqlBetweenTestData.Request(database,
                        "SELECT * FROM orders WHERE number BETWEEN lowerBound AND 9")));
                var upperDenied = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(Reader,
                    SqlBetweenTestData.Request(database,
                        "SELECT * FROM orders WHERE number BETWEEN 1 AND upperBound")));

                await Assert.That(lowerDenied.Code).IsEqualTo(ErrorCode.PermissionDenied);
                await Assert.That(upperDenied.Code).IsEqualTo(ErrorCode.PermissionDenied);
                await TeamRowPolicyAssertions.UnchangedAsync(database.Store, image, position);
                await Assert.That(database.Store.Position).IsEqualTo(position);

                var adminControl = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database,
                    AuthorizedFieldBoundsSql));

                await Assert.That(SqlBetweenTestData.Ids(adminControl)).IsEqualTo(SqlBetweenTestData.RowMiddle);
                await Assert.That(adminControl.CutPosition).IsEqualTo(position);
                await Assert.That(database.Store.Position).IsEqualTo(position);
                await SqlBetweenAuthorityContinuation.RunAsync(database);
            }
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
            {
                failures.Add(error);
            }
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
            {
                failures.Add(error);
            }
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    [Test]
    public async Task AcSqlc006APreCancelledRangeDoesNotPreventTheNextRequest()
    {
        using var database = SqlBetweenTestData.Create();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cancelled = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(SqlBetweenTestData.Root,
            SqlBetweenTestData.Request(database, RangeSql), cancellationToken: cancellation.Token));
        var recovered = engine.Execute(SqlBetweenTestData.Root,
            SqlBetweenTestData.Request(database, "SELECT * FROM orders WHERE id = 'middle' AND number BETWEEN 1 AND 9"));

        await Assert.That(cancelled.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(SqlBetweenTestData.Ids(recovered)).IsEqualTo(SqlBetweenTestData.RowMiddle);
    }

    private static void ConfigureReader(TestDatabase database)
        => database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(Reader, Tenant,
            [new(Database, SqlBetweenTestData.Collection, Capability.DocumentsRead | Capability.Query)], [])))
            .Get<PrincipalRecord>();
}
