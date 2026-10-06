using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class C1OutcomeInspectionHealthyFollowUp
{
    private const string HealthyPartitionKey = "inspection-healthy-follow-up";
    private const string HealthyDocumentId = "inspection-healthy-follow-up-document";
    private const string HealthyDocumentJson = "{\"followUp\":true}";
    private const string SeedDocumentJson = "{\"seed\":true}";

    internal static DatabaseEngine CreateDatabaseEngine(ZoneTreeStore nativeStore)
        => new(nativeStore, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(),
            UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(),
            UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());

    internal static void CommitBatch(C1OutcomeInspectionFixture fixture, PartitionRef partition,
        Guid commandId, string documentId)
    {
        var command = new CommandRequest(commandId, partition,
            [new PutDocument(C1OutcomeInspectionFixture.ResourceName, documentId, SeedDocumentJson)]);
        var operation = new ReplicatedOperation(commandId, OperationKind.Batch,
            C1OutcomeInspectionAssertions.AdminId, fixture.Database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(command, JsonDefaults.Options));
        var result = fixture.Database.Apply(operation);
        if (result.Error is not null || fixture.Database.ResolveOutcome(operation).Error is not null)
        { throw new InvalidOperationException(C1OutcomeInspectionFixture.SuccessMessage); }
        fixture.RecordPosition(fixture.Store.Position);
    }

    internal static async Task AssertUnrelatedCurrentOperationAsync(C1OutcomeInspectionFixture fixture,
        byte[]? expectedCorruptOutcome, byte[]? expectedCorruptLocator)
    {
        using var store = ZoneTreeExistingStore.Open(new ZoneTreeStoreOptions(fixture.DirectoryPath)
        { Incarnation = fixture.Incarnation }, fixture.Identity.NodeId, UnitExecutionOptions.StorageExecution());
        var corruptOutcomeKey = KeySpace.PartitionOutcome(fixture.Partition,
            C1OutcomeInspectionAssertions.AdminId, fixture.CommandId);
        var corruptLocatorKey = KeySpace.OutcomeLocatorV2(fixture.Partition,
            C1OutcomeInspectionAssertions.AdminId, fixture.CommandId);
        var partition = fixture.Partition with { PartitionKey = HealthyPartitionKey };
        var commandId = Guid.NewGuid();
        var command = new CommandRequest(commandId, partition,
            [new PutDocument(C1OutcomeInspectionFixture.ResourceName, HealthyDocumentId, HealthyDocumentJson)]);
        var database = CreateDatabaseEngine(store);
        var operation = new ReplicatedOperation(commandId, OperationKind.Batch,
            C1OutcomeInspectionAssertions.AdminId, database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(command, JsonDefaults.Options));
        var applied = database.Apply(operation);
        var resolved = database.ResolveOutcome(operation);
        var stored = store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(partition,
            C1OutcomeInspectionAssertions.AdminId, commandId)));
        var locator = store.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocatorV2(partition,
            C1OutcomeInspectionAssertions.AdminId, commandId)));
        var document = database.GetDocument(C1OutcomeInspectionAssertions.AdminId,
            new(partition, C1OutcomeInspectionFixture.ResourceName, HealthyDocumentId));
        var corruptOutcomeAfter = store.Read(view => view.ReadOwnedValue(corruptOutcomeKey));
        var corruptLocatorAfter = store.Read(view => view.ReadOwnedValue(corruptLocatorKey));

        await Assert.That(applied.Error).IsNull();
        await Assert.That(resolved.Error).IsNull();
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored!.Result).IsEqualTo(applied);
        await Assert.That(stored.Result).IsEqualTo(resolved);
        await Assert.That(stored.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(stored.Partition).IsEqualTo(partition);
        await Assert.That(stored.Result.Error).IsNull();
        await Assert.That(stored.Result.Json).IsEqualTo(resolved.Json);
        await Assert.That(document?.Json).IsEqualTo(HealthyDocumentJson);
        await Assert.That(document?.Revision).IsEqualTo(1L);
        await Assert.That(locator is not null && locator.AsSpan().SequenceEqual(
            KeySpace.PartitionOutcome(partition, C1OutcomeInspectionAssertions.AdminId, commandId))).IsTrue();
        await Assert.That(EqualBytes(expectedCorruptOutcome, corruptOutcomeAfter)).IsTrue();
        await Assert.That(EqualBytes(expectedCorruptLocator, corruptLocatorAfter)).IsTrue();
    }

    private static bool EqualBytes(byte[]? left, byte[]? right)
        => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
}
