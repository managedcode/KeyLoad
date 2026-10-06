using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class CommandOutcomeAuthorizationOrderScenario
{
    private const string Writer = "outcome-corruption-writer";
    private const string DocumentId = "authorization-order";
    private const string DocumentJson = "{\"value\":1}";
    private const string HealthyFollowupDocumentId = "authorization-order-followup";
    private const string HealthyFollowupJson = "{\"value\":2}";
    private const int InvalidScopeValue = 999;

    internal static async Task RunAsync()
    {
        using var database = new TestDatabase();
        database.Configure(CommandOutcomePartitionScopeTestData.Resource, ResourceKind.Collection);
        var principal = CreateWriter(database);
        var commandId = Guid.NewGuid();
        var operation = CreateWrite(database, commandId);
        await Assert.That(database.Database.Apply(operation).Error).IsNull();
        var outcomeKey = CorruptCurrentPartitionOutcome(database, commandId);
        var original = database.Store.Read(view => view.ReadOwnedValue(outcomeKey)!);
        var locatorKey = KeySpace.OutcomeLocatorV2(database.Partition, Writer, commandId);
        var locator = database.Store.Read(view => view.ReadOwnedValue(locatorKey)!);

        Configure(database, principal with { Grants = [], PolicyEpoch = principal.PolicyEpoch + 1 });
        await AssertDeniedPreservesOutcomeAsync(database, operation, outcomeKey, original, locatorKey, locator);
        Configure(database, principal with { PolicyEpoch = principal.PolicyEpoch + 2 });
        await AssertAuthorizedCorruptionPreservesStateAsync(database, operation, outcomeKey, original,
            locatorKey, locator, commandId);
        var healthyFollowup = CreateWrite(database, Guid.NewGuid(), HealthyFollowupDocumentId, HealthyFollowupJson);
        var followupResult = database.Database.Apply(healthyFollowup);
        await Assert.That(followupResult.Error).IsNull();
        await Assert.That(database.Database.GetDocument(Writer, new(database.Partition,
            CommandOutcomePartitionScopeTestData.Resource, HealthyFollowupDocumentId))?.Json).IsEqualTo(HealthyFollowupJson);
    }

    private static PrincipalRecord CreateWriter(TestDatabase database)
    {
        var writer = new PrincipalRecord(Writer, database.Partition.TenantId,
            [new(database.Partition.DatabaseId, CommandOutcomePartitionScopeTestData.Resource, Capability.DocumentsWrite)], []);
        return database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(writer)).Get<PrincipalRecord>();
    }

    private static ReplicatedOperation CreateWrite(TestDatabase database, Guid commandId)
        => CreateWrite(database, commandId, DocumentId, DocumentJson);

    private static ReplicatedOperation CreateWrite(TestDatabase database, Guid commandId, string documentId,
        string documentJson)
        => CommandOutcomePartitionScopeTestData.Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, database.Partition,
                [new PutDocument(CommandOutcomePartitionScopeTestData.Resource, documentId, documentJson)]))
            with
        { PrincipalId = Writer };

    private static byte[] CorruptCurrentPartitionOutcome(TestDatabase database, Guid commandId)
    {
        var outcomeKey = KeySpace.PartitionOutcome(database.Partition, Writer, commandId);
        database.Store.Commit((transaction, _) =>
        {
            var stored = transaction.GetRecord<StoredOutcome>(outcomeKey)!;
            transaction.PutRecord(outcomeKey, stored with
            { ScopeKind = (CommandOutcomeScopeKind)InvalidScopeValue, Partition = null });
            return true;
        });
        return outcomeKey;
    }

    private static void Configure(TestDatabase database, PrincipalRecord principal)
        => database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();

    private static async Task AssertDeniedPreservesOutcomeAsync(TestDatabase database, ReplicatedOperation operation,
        byte[] outcomeKey, byte[] original, byte[] locatorKey, byte[] locator)
    {
        var denied = database.Database.Apply(operation);
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await AssertOutcomeAndDocumentUnchangedAsync(database, outcomeKey, original, locatorKey, locator);
    }

    private static async Task AssertAuthorizedCorruptionPreservesStateAsync(TestDatabase database,
        ReplicatedOperation operation, byte[] outcomeKey, byte[] original, byte[] locatorKey, byte[] locator,
        Guid commandId)
    {
        var before = database.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Apply(operation));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
        await AssertOutcomeAndDocumentUnchangedAsync(database, outcomeKey, original, locatorKey, locator);
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.UnknownOutcome(Writer, commandId)))).IsNull();
    }

    private static async Task AssertOutcomeAndDocumentUnchangedAsync(TestDatabase database, byte[] outcomeKey,
        byte[] original, byte[] locatorKey, byte[] locator)
    {
        var after = database.Store.Read(view => view.ReadOwnedValue(outcomeKey)!);
        var actualLocator = database.Store.Read(view => view.ReadOwnedValue(locatorKey)!);
        var document = database.Store.Read(view => view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(
            database.Partition, CommandOutcomePartitionScopeTestData.Resource, DocumentId)));
        await Assert.That(original.AsSpan().SequenceEqual(after)).IsTrue();
        await Assert.That(locator.AsSpan().SequenceEqual(actualLocator)).IsTrue();
        await Assert.That(document?.Json).IsEqualTo(DocumentJson);
    }
}
