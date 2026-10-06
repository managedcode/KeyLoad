using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class CommandOutcomePartitionScopeFailureTests
{
    [Test]
    public async Task UnknownFailureHasItsOwnIdentityBeforeAndAfterSameIdPartitionOperations()
    {
        using var database = new TestDatabase();
        database.Configure(CommandOutcomePartitionScopeTestData.Resource, ResourceKind.Collection);
        var commandId = Guid.NewGuid();
        var malformed = new ReplicatedOperation(commandId, OperationKind.ConfigureResource, "root",
            database.Database.EvaluationClock.GetUtcNow(), "{");
        var malformedResult = database.Database.Apply(malformed);
        var unknownKey = KeySpace.UnknownOutcome("root", commandId);

        var first = CommandOutcomePartitionScopeTestData.Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, database.Partition, [new PutDocument(CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.FirstDocument, CommandOutcomePartitionScopeTestData.FirstJson)]));
        var secondPartition = new PartitionRef(database.Partition.TenantId, database.Partition.DatabaseId,
            database.Partition.TransactionDomainId, CommandOutcomePartitionScopeTestData.OtherAtomicPartition);
        var second = CommandOutcomePartitionScopeTestData.Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, secondPartition, [new PutDocument(CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.SecondDocument, CommandOutcomePartitionScopeTestData.SecondJson)]));
        var firstResult = database.Database.Apply(first);
        var secondResult = database.Database.Apply(second);
        var unknownReplay = database.Database.Apply(malformed);
        var changedMalformed = database.Database.Apply(malformed with { PayloadJson = "[" });

        await Assert.That(malformedResult.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(firstResult.Error).IsNull();
        await Assert.That(secondResult.Error).IsNull();
        await Assert.That(unknownReplay.Error).IsEqualTo(malformedResult.Error);
        await Assert.That(unknownReplay.SafeDetail).IsEqualTo(malformedResult.SafeDetail);
        await Assert.That(changedMalformed.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(database.Store.Read(view => view.GetRecord<StoredOutcome>(unknownKey))?.ScopeKind)
            .IsEqualTo(CommandOutcomeScopeKind.Unknown);
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.LegacyOutcomeKey("root", commandId)))).IsNull();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.OutcomeLocatorV2(database.Partition, "root", commandId)))).IsNotNull();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.OutcomeLocatorV2(secondPartition, "root", commandId)))).IsNotNull();
        await Assert.That(database.Database.GetDocument("root",
            new(database.Partition, CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.FirstDocument))!.Json).IsEqualTo(CommandOutcomePartitionScopeTestData.FirstJson);
        await Assert.That(database.Database.GetDocument("root",
            new(secondPartition, CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.SecondDocument))!.Json).IsEqualTo(CommandOutcomePartitionScopeTestData.SecondJson);
    }

    [Test]
    public async Task MissingOrMismatchedV2LocatorIsCorruptionAndNeverRepairedOnReplay()
    {
        using var database = new TestDatabase();
        database.Configure(CommandOutcomePartitionScopeTestData.Resource, ResourceKind.Collection);
        var commandId = Guid.NewGuid();
        var operation = CommandOutcomePartitionScopeTestData.Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, database.Partition, [new PutDocument(CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.FirstDocument, CommandOutcomePartitionScopeTestData.FirstJson)]));
        _ = database.Database.Apply(operation);
        var key = KeySpace.OutcomeLocatorV2(database.Partition, "root", commandId);
        database.Store.Commit((transaction, _) => { transaction.Delete(key); return true; });
        await CommandOutcomePartitionScopeTestData.AssertCorruptionWithoutMutationAsync(database, operation);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, KeySpace.PartitionOutcome(database.Partition, "root", Guid.NewGuid()));
            return true;
        });
        await CommandOutcomePartitionScopeTestData.AssertCorruptionWithoutMutationAsync(database, operation);
        database.Store.Commit((transaction, _) =>
        {
            var outcome = transaction.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(database.Partition, "root", commandId))!;
            transaction.PutRecord(KeySpace.PartitionOutcome(database.Partition, "root", commandId), outcome with
            {
                Partition = new PartitionRef(database.Partition.TenantId, database.Partition.DatabaseId,
                    database.Partition.TransactionDomainId, CommandOutcomePartitionScopeTestData.OtherAtomicPartition)
            });
            return true;
        });
        await CommandOutcomePartitionScopeTestData.AssertCorruptionWithoutMutationAsync(database, operation);
        database.Store.Commit((transaction, _) =>
        {
            var outcome = transaction.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(database.Partition, "root", commandId))!;
            transaction.PutRecord(KeySpace.PartitionOutcome(database.Partition, "root", commandId), outcome with
            { ScopeKind = (CommandOutcomeScopeKind)999, Partition = null });
            return true;
        });
        await CommandOutcomePartitionScopeTestData.AssertCorruptionWithoutMutationAsync(database, operation);
    }

    [Test]
    public async Task OrphanV1OrV2LocatorBlocksNewWriteWithoutRepair()
    {
        using var database = new TestDatabase();
        database.Configure(CommandOutcomePartitionScopeTestData.Resource, ResourceKind.Collection);
        var commandId = Guid.NewGuid();
        var operation = CommandOutcomePartitionScopeTestData.Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, database.Partition,
                [new PutDocument(CommandOutcomePartitionScopeTestData.Resource, "orphan", "{}")]));
        var legacyLocator = KeySpace.OutcomeLocatorV1(database.Partition, "root", commandId);
        var scopedLocator = KeySpace.OutcomeLocatorV2(database.Partition, "root", commandId);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Put(legacyLocator, KeySpace.LegacyOutcomeKey("root", commandId));
            transaction.Put(scopedLocator, KeySpace.PartitionOutcome(database.Partition, "root", commandId));
            return true;
        });
        var before = database.Store.Position;
        var result = database.Database.Apply(operation);

        await Assert.That(result.Error).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.PartitionOutcome(database.Partition, "root", commandId)))).IsNull();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(legacyLocator))).IsNotNull();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(scopedLocator))).IsNotNull();
    }

    [Test]
    public async Task RetainedLegacyUnknownOutcomeIsAnAmbiguityBarrierAndIsNotRewritten()
    {
        using var database = new TestDatabase();
        database.Configure(CommandOutcomePartitionScopeTestData.Resource, ResourceKind.Collection);
        var commandId = Guid.NewGuid();
        var malformed = new ReplicatedOperation(commandId, OperationKind.ConfigureResource, "root",
            database.Database.EvaluationClock.GetUtcNow(), "{");
        var malformedResult = database.Database.Apply(malformed);
        var legacyKey = KeySpace.LegacyOutcomeKey("root", commandId);
        var unknownKey = KeySpace.UnknownOutcome("root", commandId);
        var previous = database.Store.Read(view => view.GetRecord<StoredOutcome>(unknownKey))!;
        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(legacyKey, previous with { ScopeKind = CommandOutcomeScopeKind.Unknown, Partition = null });
            transaction.Delete(unknownKey);
            return true;
        });

        var valid = CommandOutcomePartitionScopeTestData.Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, database.Partition,
                [new PutDocument(CommandOutcomePartitionScopeTestData.Resource,
                    CommandOutcomePartitionScopeTestData.FirstDocument, CommandOutcomePartitionScopeTestData.FirstJson)]));
        var beforeReplay = database.Store.Position;
        var replay = database.Database.Apply(malformed);
        var changed = database.Database.Apply(malformed with { PayloadJson = "[" });
        var blockedScopedWrite = database.Database.Apply(valid);

        await Assert.That(malformedResult.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(replay.Error).IsEqualTo(malformedResult.Error);
        await Assert.That(replay.SafeDetail).IsEqualTo(malformedResult.SafeDetail);
        await Assert.That(changed.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(blockedScopedWrite.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(database.Store.Position).IsEqualTo(beforeReplay);
        await Assert.That(database.Store.Read(view => view.GetRecord<StoredOutcome>(legacyKey)?.ScopeKind))
            .IsEqualTo(CommandOutcomeScopeKind.Unknown);
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(unknownKey))).IsNull();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.PartitionOutcome(database.Partition, "root", commandId)))).IsNull();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.OutcomeLocatorV2(database.Partition, "root", commandId)))).IsNull();
    }

}
