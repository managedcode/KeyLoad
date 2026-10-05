using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class CommandOutcomePartitionScopeTests
{
    private const string Resource = "outcome-documents";
    private const string OtherAtomicPartition = "other-partition";

    [Test]
    public async Task PartitionOutcomeAndLocatorCommitAndReplayTogether()
    {
        using var database = new TestDatabase();
        database.Configure(Resource, ResourceKind.Collection);
        var commandId = Guid.NewGuid();
        var request = new CommandRequest(commandId, database.Partition, [new PutDocument(Resource, "item", "{}")]);
        var operation = Operation(database, commandId, OperationKind.Batch, request);
        var first = database.Database.Apply(operation);
        var locatorKey = KeySpace.OutcomeLocator(database.Partition, "root", commandId);
        var locator = database.Store.Read(view => view.ReadOwnedValue(locatorKey));
        var outcome = ReadOutcome(database, commandId);
        var replay = database.Database.Apply(operation);

        await Assert.That(first.Error).IsNull();
        await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(outcome.Partition).IsEqualTo(database.Partition);
        await Assert.That(locator).IsNotNull();
        await Assert.That(locator!.SequenceEqual(KeySpace.Outcome("root", commandId))).IsTrue();
        var expected = JsonSerializer.Serialize(first.Get<CommitReceipt>(), JsonDefaults.Options);
        await Assert.That(JsonSerializer.Serialize(replay.Get<CommitReceipt>(), JsonDefaults.Options)).IsEqualTo(expected);
        await Assert.That(JsonSerializer.Serialize(database.Database.ResolveOutcome(operation).Get<CommitReceipt>(),
            JsonDefaults.Options)).IsEqualTo(expected);
    }

    [Test]
    public async Task PersistedDomainFailureStillHasPartitionLocatorWithoutDomainWrite()
    {
        using var database = new TestDatabase();
        var commandId = Guid.NewGuid();
        var request = new CommandRequest(commandId, database.Partition,
            [new PutDocument("unconfigured-resource", "item", "{}")]);
        var result = database.Database.Apply(Operation(database, commandId, OperationKind.Batch, request));
        var outcome = ReadOutcome(database, commandId);

        await Assert.That(result.Error).IsEqualTo(ErrorCode.NotFound);
        await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(outcome.Partition).IsEqualTo(database.Partition);
        await Assert.That(ReadLocator(database, commandId)).IsNotNull();
        await Assert.That(database.Store.Read(view => view.GetRecord<DocumentRecord>(
            DocumentStorageKeys.RecordKey(database.Partition, "unconfigured-resource", "item")))).IsNull();
    }

    [Test]
    public async Task GlobalOutcomeRemainsGloballyKeyedWithoutPartitionLocator()
    {
        using var database = new TestDatabase();
        var commandId = Guid.NewGuid();
        var request = new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId,
            new ResourceDefinition(Resource, ResourceKind.Collection, database.Partition.TransactionDomainId));
        var operation = Operation(database, commandId, OperationKind.ConfigureResource, request);
        var result = database.Database.Apply(operation);
        var outcome = ReadOutcome(database, commandId);

        await Assert.That(result.Error).IsNull();
        await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Global);
        await Assert.That(outcome.Partition).IsNull();
        await Assert.That(ReadLocator(database, commandId)).IsNull();
        var conflicting = Operation(database, commandId, OperationKind.ConfigureResource,
            request with { Definition = new ResourceDefinition("other", ResourceKind.Collection, database.Partition.TransactionDomainId) });
        await Assert.That(database.Database.Apply(conflicting).Error).IsEqualTo(ErrorCode.Conflict);
    }

    [Test]
    public async Task MissingOrMismatchedLocatorIsCorruptionAndNeverRepairedOnReplay()
    {
        using var database = new TestDatabase();
        database.Configure(Resource, ResourceKind.Collection);
        var commandId = Guid.NewGuid();
        var operation = Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, database.Partition, [new PutDocument(Resource, "item", "{}")]));
        _ = database.Database.Apply(operation);
        var key = KeySpace.OutcomeLocator(database.Partition, "root", commandId);
        database.Store.Commit((transaction, _) => { transaction.Delete(key); return true; });
        var missing = database.Database.ResolveOutcome(operation);

        await Assert.That(missing.Error).IsEqualTo(ErrorCode.Corruption);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, KeySpace.Outcome("root", Guid.NewGuid()));
            return true;
        });
        var mismatched = database.Database.ResolveOutcome(operation);
        await Assert.That(mismatched.Error).IsEqualTo(ErrorCode.Corruption);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, KeySpace.Outcome("root", commandId));
            var outcome = transaction.GetRecord<StoredOutcome>(KeySpace.Outcome("root", commandId))!;
            transaction.PutRecord(KeySpace.Outcome("root", commandId), outcome with
            {
                Partition = new PartitionRef(database.Partition.TenantId, database.Partition.DatabaseId,
                database.Partition.TransactionDomainId, OtherAtomicPartition)
            });
            return true;
        });
        await Assert.That(database.Database.ResolveOutcome(operation).Error).IsEqualTo(ErrorCode.Corruption);
        database.Store.Commit((transaction, _) =>
        {
            var outcome = transaction.GetRecord<StoredOutcome>(KeySpace.Outcome("root", commandId))!;
            transaction.PutRecord(KeySpace.Outcome("root", commandId), outcome with
            { ScopeKind = (CommandOutcomeScopeKind)999, Partition = null });
            return true;
        });
        var unknownValue = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Outcome("root", commandId));
        await Assert.That(unknownValue.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task PrincipalCommandIdentityConflictsAcrossAtomicPartitions()
    {
        using var database = new TestDatabase();
        database.Configure(Resource, ResourceKind.Collection);
        var commandId = Guid.NewGuid();
        var firstRequest = new CommandRequest(commandId, database.Partition,
            [new PutDocument(Resource, "first", "{}")]);
        var first = Operation(database, commandId, OperationKind.Batch, firstRequest);
        await Assert.That(database.Database.Apply(first).Error).IsNull();
        var otherPartition = new PartitionRef(database.Partition.TenantId, database.Partition.DatabaseId,
            database.Partition.TransactionDomainId, OtherAtomicPartition);
        var second = Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, otherPartition, [new PutDocument(Resource, "second", "{}")]));

        await Assert.That(database.Database.Apply(second).Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(ReadLocator(database, commandId)).IsNotNull();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.OutcomeLocator(otherPartition, "root", commandId)))).IsNull();
        await Assert.That(database.Store.Read(view => view.GetRecord<DocumentRecord>(
            DocumentStorageKeys.RecordKey(otherPartition, Resource, "second")))).IsNull();
    }

    [Test]
    public async Task UnknownOutcomeRemainsUnknownAndMalformedPersistedInputGetsNoLocator()
    {
        using var database = new TestDatabase();
        database.Configure(Resource, ResourceKind.Collection);
        var commandId = Guid.NewGuid();
        var operation = Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, database.Partition, [new PutDocument(Resource, "legacy", "{}")]));
        _ = database.Database.Apply(operation);
        var outcomeKey = KeySpace.Outcome("root", commandId);
        var locatorKey = KeySpace.OutcomeLocator(database.Partition, "root", commandId);
        database.Store.Commit((transaction, _) =>
        {
            var previous = transaction.GetRecord<StoredOutcome>(outcomeKey)!;
            transaction.PutRecord(outcomeKey, previous with { ScopeKind = CommandOutcomeScopeKind.Unknown, Partition = null });
            transaction.Delete(locatorKey);
            return true;
        });
        var replay = database.Database.Apply(operation);
        var retained = ReadOutcome(database, commandId);

        await Assert.That(replay.Error).IsNull();
        await Assert.That(retained.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Unknown);
        await Assert.That(retained.Partition).IsNull();
        await Assert.That(ReadLocator(database, commandId)).IsNull();

        var malformedId = Guid.NewGuid();
        var malformed = new ReplicatedOperation(malformedId, OperationKind.Batch, "root",
            database.Database.EvaluationClock.GetUtcNow(), "{");
        await Assert.That(database.Database.Apply(malformed).Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(ReadOutcome(database, malformedId).ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Unknown);
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.OutcomeLocator(database.Partition, "root", malformedId)))).IsNull();
    }

    private static ReplicatedOperation Operation<T>(TestDatabase database, Guid id, OperationKind kind, T request)
        => new(id, kind, "root", database.Database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(request, JsonDefaults.Options));

    private static StoredOutcome ReadOutcome(TestDatabase database, Guid commandId)
        => database.Store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.Outcome("root", commandId)))!;

    private static byte[]? ReadLocator(TestDatabase database, Guid commandId)
        => database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.OutcomeLocator(database.Partition, "root", commandId)));
}
