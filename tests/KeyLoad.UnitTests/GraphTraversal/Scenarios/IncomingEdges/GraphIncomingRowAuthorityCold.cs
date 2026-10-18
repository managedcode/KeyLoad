using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphIncomingRowAuthorityCold
{
    private const string Root = "root";
    internal static async Task RunAsync(TestDatabase database, EntityRef source, EntityRef target,
        string reader, bool repairedTarget,
        List<(CommandRequest Request, CommitReceipt Receipt, byte[] Outcome)> repairs)
    {
        var identity = database.Store.Identity;
        var cut = database.Store.Position;
        var image = GraphShortestPathFullStoreImage.Bytes(database.Store);
        database.Store.Dispose();
        Exception? primary = null;
        var failures = new List<Exception>();
        try
        {
            using var cold = new ZoneTreeStore(new(database.Directory), database.StorageExecution,
                UnitExecutionOptions.PointCacheExecution(), timeProvider: database.Database.EvaluationClock);
            try
            {
                var engine = CreateOriginalEngine(database, cold);
                await Assert.That(cold.Identity.NodeId).IsEqualTo(identity.NodeId);
                await Assert.That(cold.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                await Assert.That(cold.Identity.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
                await Assert.That(cold.Position).IsEqualTo(cut);
                await Assert.That(GraphShortestPathFullStoreImage.Bytes(cold)).IsEquivalentTo(image);
                await GraphIncomingRowAuthorityAssertions.HealthyAsync(engine, cold, source, target, reader, repairedTarget);
                await ReplayAsync(engine, cold, repairs);
                await GraphIncomingRowAuthorityAssertions.HealthyAsync(engine, cold, source, target, reader, repairedTarget);
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

    private static DatabaseEngine CreateOriginalEngine(TestDatabase database, ZoneTreeStore store)
    {
        var create = TestDatabaseEngineFactory.Capture(UnitExecutionOptions.DatabaseLimits(),
            UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), null, database.Database.EvaluationClock);
        return create(store, null);
    }

    private static async Task ReplayAsync(DatabaseEngine engine, ZoneTreeStore store,
        List<(CommandRequest Request, CommitReceipt Receipt, byte[] Outcome)> repairs)
    {
        foreach (var repair in repairs)
        {
            var image = GraphShortestPathFullStoreImage.Bytes(store);
            var cut = store.Position;
            var operation = new ReplicatedOperation(repair.Request.CommandId, OperationKind.Batch, Root, default,
                JsonSerializer.Serialize(repair.Request, JsonDefaults.Options));
            var replay = engine.ApplyEmbedded(operation, cancellationToken: default).Get<CommitReceipt>();
            await Assert.That(JsonSerializer.Serialize(replay, JsonDefaults.Options))
                .IsEqualTo(JsonSerializer.Serialize(repair.Receipt, JsonDefaults.Options));
            var outcome = store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(
                repair.Request.Partition, Root, repair.Request.CommandId)));
            await Assert.That(outcome).IsEquivalentTo(repair.Outcome);
            await Assert.That(GraphShortestPathFullStoreImage.Bytes(store)).IsEquivalentTo(image);
            await Assert.That(store.Position).IsEqualTo(cut);
        }
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
