using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class DeniedCommandRepairContinuation
{
    private const long InitialOwnershipEpoch = 1;
    private const long NextPosition = 1;
    private const long RepairEpochAdvance = 2;
    private const long RepairedRevision = 3;
    private const long ColdRevision = 4;
    private const string Root = "root";
    private const string Collection = "denied-replay-documents";
    private const string Document = "healthy";
    private const string RepairedJson = "{\"healthy\":true,\"phase\":\"repaired\"}";
    private const string ColdJson = "{\"healthy\":true,\"phase\":\"cold\"}";
    private const string ChangedPolicy = "The principal policy changed since this command was evaluated.";

    internal static async Task OriginalReceiptAsync(TestDatabase fixture, CommitReceipt receipt,
        Guid commandId, long previousPosition)
        => await ReceiptAsync(fixture.Store, fixture.Partition, receipt, commandId, previousPosition, NextPosition);

    internal static async Task RunAsync(TestDatabase fixture, PrincipalRecord writer,
        ReplicatedOperation original, byte[] outcome)
    {
        fixture.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(writer with
            {
                PolicyEpoch = writer.PolicyEpoch + RepairEpochAdvance
            })).Get<PrincipalRecord>();
        await OldDeniedAsync(fixture.Database, fixture.Store, fixture.Partition, original, outcome);
        var current = Fresh(fixture.Database, fixture.Partition, writer.Id, RepairedRevision, RepairedJson);
        var position = fixture.Store.Position;
        var result = fixture.Database.Apply(current);
        await ReceiptAsync(fixture.Store, fixture.Partition, result.Get<CommitReceipt>(), current.Id, position, RepairedRevision);
        await DocumentAsync(fixture.Database, fixture.Partition, RepairedRevision, RepairedJson);
        await ReplayAsync(fixture.Database, fixture.Store, current, result);
        await OldDeniedAsync(fixture.Database, fixture.Store, fixture.Partition, original, outcome);
        await ColdAsync(fixture, original, outcome, current, result);
    }

    private static async Task ColdAsync(TestDatabase fixture, ReplicatedOperation original,
        byte[] outcome, ReplicatedOperation current, OperationResult result)
    {
        var identity = fixture.Store.Identity;
        var position = fixture.Store.Position;
        var bytes = QueueWholeFlowStorage.Bytes(fixture.Store);
        fixture.JoinOwnedResources();
        var failures = new List<Exception>();
        try
        {
            using var reopened = new ZoneTreeStore(new(fixture.Directory),
                UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            try
            {
                await Assert.That(reopened.Identity.NodeId).IsEqualTo(identity.NodeId);
                await Assert.That(reopened.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                await Assert.That(reopened.Identity.FormatVersion).IsEqualTo(identity.FormatVersion);
                await Assert.That(reopened.Identity.KeyCodecVersion).IsEqualTo(identity.KeyCodecVersion);
                await Assert.That(reopened.Identity.Durability).IsEqualTo(identity.Durability);
                await Assert.That(reopened.Identity.DispatchPaused).IsEqualTo(identity.DispatchPaused);
                await Assert.That(reopened.Identity.MinimumReaderContract).IsEqualTo(identity.MinimumReaderContract);
                await Assert.That(reopened.Identity.SigningKey.Span.SequenceEqual(identity.SigningKey.Span)).IsTrue();
                await Assert.That(reopened.Identity.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
                await UnchangedAsync(reopened, position, bytes);
                var database = fixture.CreateConfiguredDatabase(reopened);
                await OldDeniedAsync(database, reopened, fixture.Partition, original, outcome);
                await ReplayAsync(database, reopened, current, result);
                await DocumentAsync(database, fixture.Partition, RepairedRevision, RepairedJson);
                await ColdHealthyAsync(database, reopened, fixture.Partition, original.PrincipalId);
            }
            catch (Exception error) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        catch (Exception error) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ColdHealthyAsync(DatabaseEngine database, ZoneTreeStore store,
        PartitionRef partition, string principal)
    {
        var fresh = Fresh(database, partition, principal, ColdRevision, ColdJson);
        var position = store.Position;
        var result = database.Apply(fresh);
        await ReceiptAsync(store, partition, result.Get<CommitReceipt>(), fresh.Id, position, ColdRevision);
        await DocumentAsync(database, partition, ColdRevision, ColdJson);
        await ReplayAsync(database, store, fresh, result);
    }

    private static ReplicatedOperation Fresh(DatabaseEngine database, PartitionRef partition,
        string principal, long revision, string json)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, partition,
            [new PutDocument(Collection, Document, json, revision - NextPosition)]);
        return new(id, OperationKind.Batch, principal, database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(command, JsonDefaults.Options));
    }

    private static async Task OldDeniedAsync(DatabaseEngine database, ZoneTreeStore store,
        PartitionRef partition, ReplicatedOperation original, byte[] outcome)
    {
        var position = store.Position;
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var denied = database.Apply(original with
        {
            EvaluatedAt = database.EvaluationClock.GetUtcNow()
        });
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(denied.SafeDetail).IsEqualTo(ChangedPolicy);
        await Assert.That(denied.Json).IsNull();
        await Assert.That(denied.NativeValue).IsNull();
        await UnchangedAsync(store, position, bytes);
        var retained = store.Read(view => view.ReadOwnedValue(
            OutcomeStoreOracle.PartitionKey(partition, original.PrincipalId, original.Id)));
        await Assert.That(retained is not null && retained.SequenceEqual(outcome)).IsTrue();
    }

    private static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        ReplicatedOperation original, OperationResult expected)
    {
        var position = store.Position;
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var replay = database.Apply(original with
        {
            EvaluatedAt = database.EvaluationClock.GetUtcNow()
        });
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        await UnchangedAsync(store, position, bytes);
    }

    private static async Task ReceiptAsync(ZoneTreeStore store, PartitionRef partition,
        CommitReceipt actual, Guid commandId, long previousPosition, long revision)
    {
        var expected = new CommitReceipt(commandId,
            new(store.Identity.Incarnation, partition.AtomicPartitionId, previousPosition + NextPosition, InitialOwnershipEpoch),
            [new("putDocument", Collection, Document, revision)], store.Identity.Durability);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    private static async Task DocumentAsync(DatabaseEngine database, PartitionRef partition,
        long revision, string json)
    {
        var actual = database.GetDocument(Root, new(partition, Collection, Document));
        var expected = new DocumentResult(new(partition, Collection, Document), revision, json, false, []);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    private static async Task UnchangedAsync(ZoneTreeStore store, long position, string[] bytes)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store).SequenceEqual(bytes)).IsTrue();
    }
}
