using KeyLoad.Core;
using KeyLoad.ProcessRecoveryTests.DocumentStorage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal static class LocalInboxProcessingCrashScenario
{
    private const long MissingDocumentRevision = 0L;
    private const string ProcessedEventType = "Processed";

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var database = CrashDatabase.Create(store);
        var collection = Configure(database);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, LocalInboxProcessingCrashContract.CollectionFile, collection);
        var producer = new CommandRequest(Guid.NewGuid(), LocalInboxProcessingCrashContract.Partition,
            [new EnqueueMessage(LocalInboxProcessingCrashContract.Input, LocalInboxProcessingCrashContract.Message,
                LocalInboxProcessingCrashContract.Payload)]);
        CrashDatabase.Submit(database, OperationKind.Batch, producer, producer.CommandId).Get<CommitReceipt>();
        var receive = new ReceiveRequest(Guid.NewGuid(), LocalInboxProcessingCrashContract.InputLane);
        var delivery = CrashDatabase.Submit(database, OperationKind.Receive, receive, receive.RequestId).Get<ReceiveResult>().Deliveries.Single();
        var request = new ProcessingRequest(LocalInboxProcessingCrashContract.CommandId,
            LocalInboxProcessingCrashContract.InputLane, delivery.Token, LocalInboxProcessingCrashContract.Handler,
            LocalInboxProcessingCrashContract.Generation,
            [new PutDocument(LocalInboxProcessingCrashContract.Documents, LocalInboxProcessingCrashContract.Effect,
                LocalInboxProcessingCrashContract.Payload, MissingDocumentRevision),
             new AppendEvents(LocalInboxProcessingCrashContract.Events, LocalInboxProcessingCrashContract.Effect,
                [new(LocalInboxProcessingCrashContract.Effect, ProcessedEventType, LocalInboxProcessingCrashContract.Payload)], ExpectedStreamRevision.NoStream),
             new EnqueueMessage(LocalInboxProcessingCrashContract.Output, LocalInboxProcessingCrashContract.Message,
                LocalInboxProcessingCrashContract.Payload)]);
        var operation = CrashDatabase.Operation(database, OperationKind.Processing, request, request.CommandId);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, LocalInboxProcessingCrashContract.OperationFile, operation);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, LocalInboxProcessingCrashContract.InputFile,
            database.InspectMessage(CrashFixtureValues.Principal, LocalInboxProcessingCrashContract.InputLane,
                LocalInboxProcessingCrashContract.Message)!);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, LocalInboxProcessingCrashContract.PositionFile, store.Position);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, LocalInboxProcessingCrashContract.TargetFile,
            CrashDatabase.NextCommitToken(database, LocalInboxProcessingCrashContract.Partition));
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, LocalInboxProcessingCrashContract.TailFile,
            database.GetOutboxStatus(CrashFixtureValues.Principal, LocalInboxProcessingCrashContract.Partition).Head.Tail);
        boundary.Position = store.Position + LocalInboxProcessingCrashContract.Generation;
        boundary.Armed = true;
        database.Apply(operation).Get<CommitReceipt>();
        await RecoveryProcessPause.WaitForKillAsync();
    }

    private static Command<DatabaseCommand> Configure(DatabaseEngine database)
    {
        var operations = new List<Command<DatabaseCommand>>();
        foreach (var resource in new[]
        {
            new ResourceDefinition(LocalInboxProcessingCrashContract.Input, ResourceKind.WorkQueue, LocalInboxProcessingCrashContract.Partition.TransactionDomainId),
            new ResourceDefinition(LocalInboxProcessingCrashContract.Output, ResourceKind.WorkQueue, LocalInboxProcessingCrashContract.Partition.TransactionDomainId),
            new ResourceDefinition(LocalInboxProcessingCrashContract.Documents, ResourceKind.Collection, LocalInboxProcessingCrashContract.Partition.TransactionDomainId),
            new ResourceDefinition(LocalInboxProcessingCrashContract.Events, ResourceKind.StreamSet, LocalInboxProcessingCrashContract.Partition.TransactionDomainId)
        })
        {
            var operation = CrashDatabase.Operation(database, OperationKind.ConfigureResource, new ConfigureResourceRequest(
                LocalInboxProcessingCrashContract.Partition.TenantId, LocalInboxProcessingCrashContract.Partition.DatabaseId, resource), Guid.NewGuid());
            database.Apply(operation).Get<ResourceDefinition>();
            operations.Add(operation);
        }
        return operations.Single(operation => ((ConfigureResourceRequest)operation.Value!.Input)
            .Definition.Name == LocalInboxProcessingCrashContract.Documents);
    }
}
