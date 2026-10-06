using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class FullPartitionOutcomeIdentityScenario
{
    private const string Administrator = "root";
    private const string FirstResource = "full-identity-first";
    private const string SecondResource = "full-identity-second";
    private const string DocumentId = "same-document";
    private const string FirstJson = "{\"value\":\"first\"}";
    private const string SecondJson = "{\"value\":\"second\"}";
    private const string ChangedFirstJson = "{\"value\":\"changed-first\"}";
    private const string ChangedSecondJson = "{\"value\":\"changed-second\"}";

    internal static Task RunTenantAsync() => RunAsync(nameof(PartitionRef.TenantId));
    internal static Task RunDatabaseAsync() => RunAsync(nameof(PartitionRef.DatabaseId));
    internal static Task RunTransactionDomainAsync() => RunAsync(nameof(PartitionRef.TransactionDomainId));
    internal static Task RunPartitionKeyAsync() => RunAsync(nameof(PartitionRef.PartitionKey));

    private static async Task RunAsync(string changedComponent)
    {
        using var database = new TestDatabase();
        var firstPartition = database.Partition;
        var secondPartition = FullPartitionOutcomeIdentityAssertions.ChangedPartitionRef(firstPartition, changedComponent);
        await FullPartitionOutcomeIdentityAssertions.AssertOnlyComponentChangedAsync(firstPartition, secondPartition,
            changedComponent);
        ConfigureCollection(database, firstPartition, FirstResource);
        ConfigureCollection(database, secondPartition, SecondResource);
        var commandId = Guid.NewGuid();
        var operationTime = database.Database.EvaluationClock.GetUtcNow();
        var firstOperation = CreateOperation(commandId, firstPartition, FirstResource, FirstJson, operationTime);
        var secondOperation = CreateOperation(commandId, secondPartition, SecondResource, SecondJson, operationTime);
        var committed = await CommitReplayAndCheckConflictsAsync(database, firstOperation, secondOperation,
            firstPartition, secondPartition);

        await ReopenAndResolveAsync(database, committed, firstPartition, secondPartition);
    }

    private static void ConfigureCollection(TestDatabase database, PartitionRef partition, string resource)
    {
        var definition = new ResourceDefinition(resource, ResourceKind.Collection, partition.TransactionDomainId);
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        _ = database.Submit(OperationKind.ConfigureResource, request).Get<ResourceDefinition>();
    }

    private static ReplicatedOperation CreateOperation(Guid commandId, PartitionRef partition, string resource,
        string json, DateTimeOffset operationTime)
    {
        var request = new CommandRequest(commandId, partition, [new PutDocument(resource, DocumentId, json)]);
        return new(commandId, OperationKind.Batch, Administrator, operationTime,
            JsonSerializer.Serialize(request, JsonDefaults.Options));
    }

    private static ReplicatedOperation ChangedOperation(ReplicatedOperation original, PartitionRef partition,
        string resource, string json)
    {
        var request = new CommandRequest(original.Id, partition, [new PutDocument(resource, DocumentId, json)]);
        return original with { PayloadJson = JsonSerializer.Serialize(request, JsonDefaults.Options) };
    }

    private static async Task<(ReplicatedOperation First, ReplicatedOperation Second, byte[] FirstReceipt,
        byte[] SecondReceipt, long FirstTail, long SecondTail)> CommitReplayAndCheckConflictsAsync(TestDatabase database,
        ReplicatedOperation firstOperation, ReplicatedOperation secondOperation, PartitionRef firstPartition,
        PartitionRef secondPartition)
    {
        var firstResult = database.Database.Apply(firstOperation);
        var secondResult = database.Database.Apply(secondOperation);
        await Assert.That(firstResult.Error).IsNull();
        await Assert.That(secondResult.Error).IsNull();
        var firstReceipt = NativeSerialization.Serialize(firstResult.Get<CommitReceipt>());
        var secondReceipt = NativeSerialization.Serialize(secondResult.Get<CommitReceipt>());
        await FullPartitionOutcomeIdentityAssertions.AssertStoredOutcomeAsync(database.Store, firstPartition,
            firstOperation.Id, firstReceipt);
        await FullPartitionOutcomeIdentityAssertions.AssertStoredOutcomeAsync(database.Store, secondPartition,
            secondOperation.Id, secondReceipt);
        await FullPartitionOutcomeIdentityAssertions.AssertDocumentAsync(database.Database, firstPartition,
            FirstResource, DocumentId, FirstJson);
        await FullPartitionOutcomeIdentityAssertions.AssertDocumentAsync(database.Database, secondPartition,
            SecondResource, DocumentId, SecondJson);
        var firstTail = database.Database.GetOutboxStatus(Administrator, firstPartition).Head.Tail;
        var secondTail = database.Database.GetOutboxStatus(Administrator, secondPartition).Head.Tail;

        var firstReplay = database.Database.Apply(firstOperation);
        var secondReplay = database.Database.Apply(secondOperation);
        await FullPartitionOutcomeIdentityAssertions.AssertReceiptAsync(firstReplay, firstReceipt);
        await FullPartitionOutcomeIdentityAssertions.AssertReceiptAsync(secondReplay, secondReceipt);
        var changedFirst = database.Database.Apply(ChangedOperation(firstOperation, firstPartition, FirstResource,
            ChangedFirstJson));
        var changedSecond = database.Database.Apply(ChangedOperation(secondOperation, secondPartition, SecondResource,
            ChangedSecondJson));
        await Assert.That(changedFirst.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(changedSecond.Error).IsEqualTo(ErrorCode.Conflict);
        await FullPartitionOutcomeIdentityAssertions.AssertStoredOutcomeAsync(database.Store, firstPartition,
            firstOperation.Id, firstReceipt);
        await FullPartitionOutcomeIdentityAssertions.AssertStoredOutcomeAsync(database.Store, secondPartition,
            secondOperation.Id, secondReceipt);
        await VerifyUnchangedAfterRetriesAsync(database, firstPartition, secondPartition, firstTail, secondTail);

        return (firstOperation, secondOperation, firstReceipt, secondReceipt, firstTail, secondTail);
    }

    private static async Task VerifyUnchangedAfterRetriesAsync(TestDatabase database, PartitionRef firstPartition,
        PartitionRef secondPartition, long firstTail, long secondTail)
    {
        await FullPartitionOutcomeIdentityAssertions.AssertDocumentAsync(database.Database, firstPartition,
            FirstResource, DocumentId, FirstJson);
        await FullPartitionOutcomeIdentityAssertions.AssertDocumentAsync(database.Database, secondPartition,
            SecondResource, DocumentId, SecondJson);
        await FullPartitionOutcomeIdentityAssertions.AssertOutboxTailsAsync(database.Database, firstPartition,
            secondPartition, firstTail, secondTail);
    }

    private static async Task ReopenAndResolveAsync(TestDatabase database,
        (ReplicatedOperation First, ReplicatedOperation Second, byte[] FirstReceipt, byte[] SecondReceipt,
            long FirstTail, long SecondTail) committed, PartitionRef firstPartition, PartitionRef secondPartition)
    {
        var directory = database.Directory;
        database.Store.Dispose();
        using var reopenedStore = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var reopened = new DatabaseEngine(reopenedStore, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.TimeSeriesExecution());
        PhysicalShardTestBootstrap.RequireExisting(reopened);
        await FullPartitionOutcomeIdentityAssertions.AssertReceiptAsync(reopened.ResolveOutcome(committed.First),
            committed.FirstReceipt);
        await FullPartitionOutcomeIdentityAssertions.AssertReceiptAsync(reopened.ResolveOutcome(committed.Second),
            committed.SecondReceipt);
        await FullPartitionOutcomeIdentityAssertions.AssertStoredOutcomeAsync(reopenedStore, firstPartition,
            committed.First.Id, committed.FirstReceipt);
        await FullPartitionOutcomeIdentityAssertions.AssertStoredOutcomeAsync(reopenedStore, secondPartition,
            committed.Second.Id, committed.SecondReceipt);
        await VerifyReopenedDocumentsAsync(reopened, firstPartition, secondPartition, committed);
        await FullPartitionOutcomeIdentityAssertions.AssertOutboxTailsAsync(reopened, firstPartition, secondPartition,
            committed.FirstTail, committed.SecondTail);
    }

    private static async Task VerifyReopenedDocumentsAsync(DatabaseEngine reopened, PartitionRef firstPartition,
        PartitionRef secondPartition, (ReplicatedOperation First, ReplicatedOperation Second, byte[] FirstReceipt,
            byte[] SecondReceipt, long FirstTail, long SecondTail) committed)
    {
        await FullPartitionOutcomeIdentityAssertions.AssertDocumentAsync(reopened, firstPartition, FirstResource,
            DocumentId, FirstJson);
        await FullPartitionOutcomeIdentityAssertions.AssertDocumentAsync(reopened, secondPartition, SecondResource,
            DocumentId, SecondJson);
        await Assert.That(committed.First.Id).IsEqualTo(committed.Second.Id);
        await Assert.That(committed.First.PrincipalId).IsEqualTo(committed.Second.PrincipalId);
    }
}
