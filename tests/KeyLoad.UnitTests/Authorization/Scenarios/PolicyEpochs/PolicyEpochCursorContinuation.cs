using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Authorization;

internal static class PolicyEpochCursorContinuation
{
    private const string Reader = "reader";
    private const string Tenant = "tenant";
    private const string DatabaseScope = "database";
    private const string Collection = "orders";
    private const long RepairedEpoch = 3;

    internal static async Task RunAsync(TestDatabase db, QueryRequest request, string originalCursor,
        CancellationToken token)
    {
        var queries = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        await PolicyEpochCursorAssertions.RefusedAsync(queries, db.Store, request, originalCursor,
            ErrorCode.Unauthenticated, token);
        var principal = new PrincipalRecord(Reader, Tenant,
            [new(DatabaseScope, Collection, Capability.DocumentsRead | Capability.Query)], [])
        {
            PolicyEpoch = RepairedEpoch
        };
        var persisted = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(persisted).AsSpan().SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
        await PolicyEpochCursorAssertions.RefusedAsync(queries, db.Store, request, originalCursor, ErrorCode.CursorExpired, token);
        var repairedCursor = await PolicyEpochCursorAssertions.PagesAsync(queries, db.Store, request, token);
        await ColdAsync(db, request, originalCursor, repairedCursor, token);
    }

    private static async Task ColdAsync(TestDatabase db, QueryRequest request, string originalCursor,
        string repairedCursor, CancellationToken token)
    {
        var identity = db.Store.Identity;
        var image = TeamRowPolicyAssertions.Image(db.Store);
        var cut = db.Store.Position;
        db.Store.Dispose();
        Exception? primary = null;
        var failures = new List<Exception>();
        try
        {
            using var cold = new ZoneTreeStore(new(db.Directory), db.StorageExecution,
                UnitExecutionOptions.PointCacheExecution(), timeProvider: db.Database.EvaluationClock);
            try
            {
                var database = db.CreateConfiguredDatabase(cold);
                await Assert.That(cold.Identity.NodeId).IsEqualTo(identity.NodeId);
                await Assert.That(cold.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                await Assert.That(cold.Identity.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
                await TeamRowPolicyAssertions.UnchangedAsync(cold, image, cut);
                var queries = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
                await PolicyEpochCursorAssertions.RefusedAsync(queries, cold, request, originalCursor, ErrorCode.CursorExpired, token);
                if (cold.Identity.ReadGeneration == identity.ReadGeneration)
                {
                    await PolicyEpochCursorAssertions.ContinuationAsync(queries, cold, request, repairedCursor, cut, token);
                }
                else
                {
                    await PolicyEpochCursorAssertions.RefusedAsync(queries, cold, request, repairedCursor, ErrorCode.CursorExpired, token);
                }
                await PolicyEpochCursorAssertions.PagesAsync(queries, cold, request, token);
            }
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
            {
                primary = error;
                throw;
            }
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
            {
                primary = error;
                throw;
            }
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            Retain(primary, error, failures);
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            Retain(primary, error, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void Retain(Exception? primary, Exception terminal, List<Exception> failures)
    {
        if (primary is not null && !ReferenceEquals(primary, terminal))
        {
            failures.Add(primary);
        }
        failures.Add(terminal);
    }
}
