using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Security;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class CommandOutcomePartitionScopeLegacyTests
{
    [Test]
    public async Task KnownDifferentScopeLegacyOutcomeDoesNotBlockPartitionV2Identity()
    {
        using var database = new TestDatabase();
        database.Configure(CommandOutcomePartitionScopeTestData.Resource, ResourceKind.Collection);
        var commandId = Guid.NewGuid();
        var globalRequest = new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition("outcome-global-resource",
                ResourceKind.Collection, database.Partition.TransactionDomainId));
        var global = CommandOutcomePartitionScopeTestData.Operation(database, commandId,
            OperationKind.ConfigureResource, globalRequest);
        var original = database.Database.Apply(global);
        var v2GlobalKey = KeySpace.GlobalOutcome("root", commandId);
        var legacyKey = KeySpace.LegacyOutcomeKey("root", commandId);
        var legacy = CommandOutcomePartitionScopeTestData.ReadStored(database, global);
        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(legacyKey, legacy);
            transaction.Delete(v2GlobalKey);
            return true;
        });
        var legacyBytes = database.Store.Read(view => view.ReadOwnedValue(legacyKey))!;

        var partition = CommandOutcomePartitionScopeTestData.Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, database.Partition,
                [new PutDocument(CommandOutcomePartitionScopeTestData.Resource,
                    CommandOutcomePartitionScopeTestData.FirstDocument, CommandOutcomePartitionScopeTestData.FirstJson)]));
        var partitionResult = database.Database.Apply(partition);
        var globalReplay = database.Database.Apply(global);

        await Assert.That(original.Error).IsNull();
        await Assert.That(partitionResult.Error).IsNull();
        await Assert.That(globalReplay.Error).IsNull();
        await Assert.That(globalReplay.Get<ResourceDefinition>().Name)
            .IsEqualTo(original.Get<ResourceDefinition>().Name);
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(v2GlobalKey))).IsNull();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(legacyKey))!.AsSpan()
            .SequenceEqual(legacyBytes)).IsTrue();
        await Assert.That(database.Store.Read(view => view.GetRecord<StoredOutcome>(
            KeySpace.PartitionOutcome(database.Partition, "root", commandId))?.ScopeKind))
            .IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(CommandOutcomePartitionScopeTestData.ReadLocator(database, database.Partition, commandId))
            .IsNotNull();
        await Assert.That(database.Database.GetDocument("root", new(database.Partition,
            CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.FirstDocument))!.Json)
            .IsEqualTo(CommandOutcomePartitionScopeTestData.FirstJson);
    }

    [Test]
    public async Task DuplicateSameScopeLegacyAndV2OutcomesAreCorruptionWithoutRepair()
    {
        using var database = new TestDatabase();
        database.Configure(CommandOutcomePartitionScopeTestData.Resource, ResourceKind.Collection);
        var commandId = Guid.NewGuid();
        var operation = CommandOutcomePartitionScopeTestData.Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, database.Partition,
                [new PutDocument(CommandOutcomePartitionScopeTestData.Resource,
                    CommandOutcomePartitionScopeTestData.FirstDocument, CommandOutcomePartitionScopeTestData.FirstJson)]));
        _ = database.Database.Apply(operation);
        var v2Key = KeySpace.PartitionOutcome(database.Partition, "root", commandId);
        var v1Key = KeySpace.LegacyOutcomeKey("root", commandId);
        var v1Locator = KeySpace.OutcomeLocatorV1(database.Partition, "root", commandId);
        var outcome = CommandOutcomePartitionScopeTestData.ReadStored(database, operation);
        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(v1Key, outcome);
            transaction.Put(v1Locator, v1Key);
            return true;
        });
        var position = database.Store.Position;
        var v1Bytes = database.Store.Read(view => view.ReadOwnedValue(v1Key))!;
        var v2Bytes = database.Store.Read(view => view.ReadOwnedValue(v2Key))!;
        var v1Index = database.Store.Read(view => view.ReadOwnedValue(v1Locator))!;
        var v2Index = database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.OutcomeLocatorV2(database.Partition, "root", commandId)))!;

        var result = database.Database.ResolveOutcome(operation);

        await Assert.That(result.Error).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(v1Key))!.AsSpan()
            .SequenceEqual(v1Bytes)).IsTrue();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(v2Key))!.AsSpan()
            .SequenceEqual(v2Bytes)).IsTrue();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(v1Locator))!.AsSpan()
            .SequenceEqual(v1Index)).IsTrue();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.OutcomeLocatorV2(database.Partition, "root", commandId)))!.AsSpan()
            .SequenceEqual(v2Index)).IsTrue();
    }
}
