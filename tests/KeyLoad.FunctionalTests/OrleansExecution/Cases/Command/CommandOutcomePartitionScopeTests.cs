using System.Text.Json;
using KeyLoad.Core.Authorization;
using KeyLoad.Core.CommandOutcomes.Contracts;
using KeyLoad.Core.DocumentStorage;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.FunctionalTests.OrleansExecution.CommandOutcomePartitionScopeTestData;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class CommandOutcomePartitionScopeTests
{
    [Test]
    public async Task PartitionOutcomeAndV2LocatorCommitAndReplayTogether()
    {
        using var database = new TestDatabase();
        database.Configure(CommandOutcomePartitionScopeTestData.Resource, ResourceKind.Collection);
        var commandId = Guid.NewGuid();
        var request = new CommandRequest(commandId, database.Partition, [new PutDocument(CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.FirstDocument, CommandOutcomePartitionScopeTestData.FirstJson)]);
        var operation = Operation(database, commandId, OperationKind.Batch, request);
        var first = database.Database.Apply(operation);
        var locatorKey = KeySpace.OutcomeLocator(database.Partition, "root", commandId);
        var locator = database.Store.Read(view => view.ReadOwnedValue(locatorKey));
        var outcome = ReadStored(database, operation);
        var replay = database.Database.Apply(operation);

        await Assert.That(Errors.Code(first.Problem).Value).IsNull();
        await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(outcome.Partition).IsEqualTo(database.Partition);
        await Assert.That(locator).IsNotNull();
        await Assert.That(locator!.SequenceEqual(KeySpace.PartitionOutcome(database.Partition, "root", commandId))).IsTrue();
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
        var operation = Operation(database, commandId, OperationKind.Batch, request);
        var result = database.Database.Apply(operation);
        var outcome = ReadStored(database, operation);

        await Assert.That(Errors.Code(result.Problem).Value).IsEqualTo(ErrorCode.NotFound);
        await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(outcome.Partition).IsEqualTo(database.Partition);
        await Assert.That(ReadLocator(database, database.Partition, commandId)).IsNotNull();
        await Assert.That(database.Store.Read(view => view.GetRecord<DocumentRecord>(
            DocumentStorageKeys.RecordKey(database.Partition, "unconfigured-resource", "item")))).IsNull();
    }

    [Test]
    public async Task GlobalAndPartitionOperationsCanReuseOnePrincipalCommandId()
    {
        using var database = new TestDatabase();
        var commandId = Guid.NewGuid();
        var globalRequest = new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId,
            new ResourceDefinition(CommandOutcomePartitionScopeTestData.Resource, ResourceKind.Collection, database.Partition.TransactionDomainId));
        var global = Operation(database, commandId, OperationKind.ConfigureResource, globalRequest);
        var globalResult = database.Database.Apply(global);

        var partitionRequest = new CommandRequest(commandId, database.Partition,
            [new PutDocument(CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.FirstDocument, CommandOutcomePartitionScopeTestData.FirstJson)]);
        var partition = Operation(database, commandId, OperationKind.Batch, partitionRequest);
        var partitionResult = database.Database.Apply(partition);

        await Assert.That(Errors.Code(globalResult.Problem).Value).IsNull();
        await Assert.That(Errors.Code(partitionResult.Problem).Value).IsNull();
        await Assert.That(ReadStored(database, global).ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Global);
        await Assert.That(ReadStored(database, partition).ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(ReadLocator(database, database.Partition, commandId)).IsNotNull();
        await Assert.That(Errors.Code(database.Database.Apply(global).Problem).Value).IsNull();
        await Assert.That(Errors.Code(database.Database.Apply(partition).Problem).Value).IsNull();
        await Assert.That(Errors.Code(database.Database.Apply(Operation(database, commandId, OperationKind.ConfigureResource,
            globalRequest with
            {
                Definition = new ResourceDefinition("other", ResourceKind.Collection,
                database.Partition.TransactionDomainId)
            })).Problem).Value).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(Errors.Code(database.Database.Apply(Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, database.Partition, [new PutDocument(CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.FirstDocument, CommandOutcomePartitionScopeTestData.SecondJson)]))).Problem).Value).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(database.Database.GetDocument("root",
            new(database.Partition, CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.FirstDocument))!.Json).IsEqualTo(CommandOutcomePartitionScopeTestData.FirstJson);
    }

    [Test]
    public async Task SamePrincipalCommandIdIsIndependentAcrossFullAtomicPartitionsAndReopen()
    {
        using var database = new TestDatabase();
        database.Configure(CommandOutcomePartitionScopeTestData.Resource, ResourceKind.Collection);
        var firstPartition = database.Partition;
        var secondPartition = new PartitionRef(firstPartition.TenantId, firstPartition.DatabaseId,
            firstPartition.TransactionDomainId, CommandOutcomePartitionScopeTestData.OtherAtomicPartition);
        var commandId = Guid.NewGuid();
        var first = Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, firstPartition, [new PutDocument(CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.FirstDocument, CommandOutcomePartitionScopeTestData.FirstJson)]));
        var second = Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, secondPartition, [new PutDocument(CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.SecondDocument, CommandOutcomePartitionScopeTestData.SecondJson)]));

        var firstResult = database.Database.Apply(first);
        var secondResult = database.Database.Apply(second);
        var firstReplay = database.Database.Apply(first);
        var secondReplay = database.Database.Apply(second);
        var changedFirst = Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, firstPartition, [new PutDocument(CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.FirstDocument, CommandOutcomePartitionScopeTestData.SecondJson)]));
        var changedSecond = Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, secondPartition, [new PutDocument(CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.SecondDocument, CommandOutcomePartitionScopeTestData.FirstJson)]));

        await Assert.That(Errors.Code(firstResult.Problem).Value).IsNull();
        await Assert.That(Errors.Code(secondResult.Problem).Value).IsNull();
        await Assert.That(firstReplay.Get<CommitReceipt>().Token).IsEqualTo(firstResult.Get<CommitReceipt>().Token);
        await Assert.That(secondReplay.Get<CommitReceipt>().Token).IsEqualTo(secondResult.Get<CommitReceipt>().Token);
        await Assert.That(Errors.Code(database.Database.Apply(changedFirst).Problem).Value).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(Errors.Code(database.Database.Apply(changedSecond).Problem).Value).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(database.Database.GetDocument("root",
            new(firstPartition, CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.FirstDocument))!.Json).IsEqualTo(CommandOutcomePartitionScopeTestData.FirstJson);
        await Assert.That(database.Database.GetDocument("root",
            new(secondPartition, CommandOutcomePartitionScopeTestData.Resource, CommandOutcomePartitionScopeTestData.SecondDocument))!.Json).IsEqualTo(CommandOutcomePartitionScopeTestData.SecondJson);
        await Assert.That(ReadLocator(database, firstPartition, commandId)!.AsSpan()
            .SequenceEqual(KeySpace.PartitionOutcome(firstPartition, "root", commandId))).IsTrue();
        await Assert.That(ReadLocator(database, secondPartition, commandId)!.AsSpan()
            .SequenceEqual(KeySpace.PartitionOutcome(secondPartition, "root", commandId))).IsTrue();

        var path = database.Directory;
        database.Store.Dispose();
        using var reopenedStore = new ZoneTreeStore(new(path), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var reopened = new DatabaseEngine(reopenedStore, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
        await Assert.That(reopened.ResolveOutcome(first).Get<CommitReceipt>().Token)
            .IsEqualTo(firstResult.Get<CommitReceipt>().Token);
        await Assert.That(reopened.ResolveOutcome(second).Get<CommitReceipt>().Token)
            .IsEqualTo(secondResult.Get<CommitReceipt>().Token);
    }
}
