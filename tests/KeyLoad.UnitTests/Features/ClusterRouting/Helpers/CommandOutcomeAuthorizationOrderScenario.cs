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

    internal static async Task RunAsync()
    {
        using var database = new TestDatabase();
        database.Configure(CommandOutcomePartitionScopeTestData.Resource, ResourceKind.Collection);
        var principal = CreateWriter(database);
        var commandId = Guid.NewGuid();
        var operation = CreateWrite(database, commandId);
        await Assert.That(database.Database.Apply(operation).Error).IsNull();
        var legacyKey = MoveOutcomeToInvalidLegacyScope(database, commandId);
        var original = database.Store.Read(view => view.ReadOwnedValue(legacyKey)!);

        Configure(database, principal with { Grants = [], PolicyEpoch = principal.PolicyEpoch + 1 });
        await AssertDeniedPreservesOutcomeAsync(database, operation, legacyKey, original, commandId);
        Configure(database, principal with { PolicyEpoch = principal.PolicyEpoch + 2 });
        await AssertAuthorizedCorruptionPreservesStateAsync(database, operation, legacyKey, original, commandId);
    }

    private static PrincipalRecord CreateWriter(TestDatabase database)
    {
        var writer = new PrincipalRecord(Writer, database.Partition.TenantId,
            [new(database.Partition.DatabaseId, CommandOutcomePartitionScopeTestData.Resource, Capability.DocumentsWrite)], []);
        return database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(writer)).Get<PrincipalRecord>();
    }

    private static ReplicatedOperation CreateWrite(TestDatabase database, Guid commandId)
        => CommandOutcomePartitionScopeTestData.Operation(database, commandId, OperationKind.Batch,
            new CommandRequest(commandId, database.Partition,
                [new PutDocument(CommandOutcomePartitionScopeTestData.Resource, DocumentId, DocumentJson)]))
            with
        { PrincipalId = Writer };

    private static byte[] MoveOutcomeToInvalidLegacyScope(TestDatabase database, Guid commandId)
    {
        var scopedKey = KeySpace.PartitionOutcome(database.Partition, Writer, commandId);
        var legacyKey = KeySpace.LegacyOutcomeKey(Writer, commandId);
        database.Store.Commit((transaction, _) =>
        {
            var stored = transaction.GetRecord<StoredOutcome>(scopedKey)!;
            transaction.Delete(scopedKey);
            transaction.Delete(KeySpace.OutcomeLocatorV2(database.Partition, Writer, commandId));
            transaction.PutRecord(legacyKey, stored with { ScopeKind = (CommandOutcomeScopeKind)999, Partition = null });
            return true;
        });
        return legacyKey;
    }

    private static void Configure(TestDatabase database, PrincipalRecord principal)
        => database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();

    private static async Task AssertDeniedPreservesOutcomeAsync(TestDatabase database, ReplicatedOperation operation,
        byte[] legacyKey, byte[] original, Guid commandId)
    {
        var denied = database.Database.Apply(operation);
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await AssertOutcomeAndDocumentUnchangedAsync(database, legacyKey, original, commandId);
    }

    private static async Task AssertAuthorizedCorruptionPreservesStateAsync(TestDatabase database,
        ReplicatedOperation operation, byte[] legacyKey, byte[] original, Guid commandId)
    {
        var before = database.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Apply(operation));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
        await AssertOutcomeAndDocumentUnchangedAsync(database, legacyKey, original, commandId);
    }

    private static async Task AssertOutcomeAndDocumentUnchangedAsync(TestDatabase database, byte[] legacyKey,
        byte[] original, Guid commandId)
    {
        var after = database.Store.Read(view => view.ReadOwnedValue(legacyKey)!);
        var document = database.Store.Read(view => view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(
            database.Partition, CommandOutcomePartitionScopeTestData.Resource, DocumentId)));
        await Assert.That(original.AsSpan().SequenceEqual(after)).IsTrue();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.PartitionOutcome(database.Partition, Writer, commandId)))).IsNull();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.OutcomeLocatorV2(database.Partition, Writer, commandId)))).IsNull();
        await Assert.That(document?.Json).IsEqualTo(DocumentJson);
    }
}
