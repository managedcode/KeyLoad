using System.Text.Json;
using KeyLoad.Core.Authorization;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class DocumentIndexCommittedScanOperations
{
    private const string RootPrincipal = "root";
    internal const int InitialDocumentRevision = 0;

    internal static DatabaseEngine CreateEngine(ZoneTreeStore store)
        => new(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(),
            UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());

    internal static CommitReceipt ApplyHealthyCommand(DatabaseEngine engine, PartitionRef partition)
    {
        var commandId = Guid.NewGuid();
        var command = new CommandRequest(commandId, partition, [new PutDocument(
            DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.HealthyId,
            DocumentIndexCommittedScanOracle.HealthyJson, InitialDocumentRevision)]);
        var operation = engine.CreateCommand(OperationKind.Batch, commandId, RootPrincipal, engine.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(command, JsonDefaults.Options));
        return engine.Apply(operation).Get<CommitReceipt>();
    }

    internal static async Task CompleteReopenedAsync(ZoneTreeStore store, PartitionRef partition,
        StoreIdentity identity, long physicalPosition, long appliedPosition)
    {
        var engine = CreateEngine(store);
        await Assert.That(engine.LastApplied).IsEqualTo(appliedPosition);
        await ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(store, identity, physicalPosition);
        await DocumentIndexCommittedScanOracle.AssertCommittedCutAsync(
            DocumentIndexCommittedScanOracle.ReadCut(store, partition), physicalPosition);
        var healthy = ApplyHealthyCommand(engine, partition);
        await Assert.That(healthy.Token.Position).IsEqualTo(appliedPosition + 1);
        await DocumentIndexCommittedScanOracle.AssertHealthyCutAsync(
            DocumentIndexCommittedScanOracle.ReadCut(store, partition), physicalPosition + 1);
    }
}
