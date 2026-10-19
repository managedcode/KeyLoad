using KeyLoad.Core;
using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.RuntimeJournaling.Contracts;
using KeyLoad.Core.Features.StoragePartitions.Contracts;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class CommandOutcomePartitionScopeFailureTests
{
    [Test]
    public async Task UnknownFailureHasItsOwnIdentityBeforeAndAfterSameIdPartitionOperations()
    {
        using var database = new TestDatabase();
        database.Configure(CommandOutcomePartitionScopeTestData.Resource, ResourceKind.Collection);
        database.Database.ConfigureRuntimeJournal(Options.Create(new RuntimeJournalOptions()));
        var commandId = Guid.NewGuid();
        var malformedInput = new RuntimeJournalMutation(RuntimeJournalAction.BootstrapIdentity,
            "outcome-invalid-bootstrap", Guid.Empty, 0, 0, null, ReadOnlyMemory<byte>.Empty, new(StringComparer.Ordinal), []);
        var malformed = CommandOutcomePartitionScopeTestData.Operation(database, commandId, OperationKind.RuntimeJournal, malformedInput);
        var malformedResult = database.Database.Apply(malformed);
        var unknownKey = KeySpace.UnknownOutcome("root", commandId);
        var originalFailure = database.Store.Read(view => view.ReadOwnedValue(unknownKey))
            ?? throw new InvalidOperationException("The original unknown-scope failure is missing.");

        var first = CommandOutcomePartitionScopeTestData.Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, database.Partition, [new PutDocument(CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.FirstDocument, CommandOutcomePartitionScopeTestData.FirstJson)]));
        var secondPartition = new PartitionRef(database.Partition.TenantId, database.Partition.DatabaseId,
            database.Partition.TransactionDomainId, CommandOutcomePartitionScopeTestData.OtherAtomicPartition);
        var second = CommandOutcomePartitionScopeTestData.Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, secondPartition, [new PutDocument(CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.SecondDocument, CommandOutcomePartitionScopeTestData.SecondJson)]));
        var firstResult = database.Database.Apply(first);
        var secondResult = database.Database.Apply(second);
        var unknownReplay = database.Database.Apply(malformed);
        var beforeRefusal = database.Store.Position;
        var changedMalformed = database.Database.Apply(CommandOutcomePartitionScopeTestData.Operation(database, commandId,
            OperationKind.RuntimeJournal, malformedInput with { JournalName = "outcome-changed-bootstrap" }));

        await Assert.That(Errors.Code(malformedResult.Problem)).IsEqualTo(ErrorCode.Validation);
        await Assert.That(Errors.Code(firstResult.Problem)).IsNull();
        await Assert.That(Errors.Code(secondResult.Problem)).IsNull();
        await Assert.That(Errors.Code(unknownReplay.Problem)).IsEqualTo(Errors.Code(malformedResult.Problem));
        await Assert.That(unknownReplay.Problem?.Detail).IsEqualTo(malformedResult.Problem?.Detail);
        await Assert.That(Errors.Code(changedMalformed.Problem)).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(database.Store.Position).IsEqualTo(beforeRefusal);
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(unknownKey)))
            .IsEquivalentTo(originalFailure, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(database.Store.Read(view => view.GetRecord<StoredOutcome>(unknownKey))?.ScopeKind)
            .IsEqualTo(CommandOutcomeScopeKind.Unknown);
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.OutcomeLocator(database.Partition, "root", commandId)))).IsNotNull();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.OutcomeLocator(secondPartition, "root", commandId)))).IsNotNull();
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
        var key = KeySpace.OutcomeLocator(database.Partition, "root", commandId);
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
            {
                ScopeKind = (CommandOutcomeScopeKind)999,
                Partition = null
            });
            return true;
        });
        await CommandOutcomePartitionScopeTestData.AssertCorruptionWithoutMutationAsync(database, operation);
    }

    [Test]
    public async Task OrphanV2LocatorBlocksNewWriteWithoutRepair()
    {
        using var database = new TestDatabase();
        database.Configure(CommandOutcomePartitionScopeTestData.Resource, ResourceKind.Collection);
        var commandId = Guid.NewGuid();
        var operation = CommandOutcomePartitionScopeTestData.Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, database.Partition,
                [new PutDocument(CommandOutcomePartitionScopeTestData.Resource, "orphan", "{}")]));
        var locator = KeySpace.OutcomeLocator(database.Partition, "root", commandId);
        var outcomeKey = KeySpace.PartitionOutcome(database.Partition, "root", commandId);
        var locatorBytes = outcomeKey.ToArray();
        database.Store.Commit((transaction, _) =>
        {
            transaction.Put(locator, locatorBytes);
            return true;
        });
        var before = database.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Apply(operation));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(outcomeKey))).IsNull();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(locator))!.AsSpan()
            .SequenceEqual(locatorBytes)).IsTrue();
    }
}
