using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

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
}
