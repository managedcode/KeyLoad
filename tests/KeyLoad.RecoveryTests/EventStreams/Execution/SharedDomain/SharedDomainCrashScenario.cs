using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using static KeyLoad.RecoveryTests.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class SharedDomainCrashScenario
{
    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var progress = new SharedDomainChildProgressRecorder(directory, store);
        progress.Record(SharedDomainChildProgressPhase.DatabaseBootstrap);
        var database = CrashDatabase.Create(store);
        progress.Record(SharedDomainChildProgressPhase.ResourceConfiguration);
        var creations = SharedDomainCrashOperations.Configure(database);
        var scopes = SharedDomainCorpus.Create(Tenant, Database);
        var time = TimeProvider.System.GetUtcNow();
        List<Command<DatabaseCommand>> operations = [];
        List<Result<object>> results = [];
        foreach (var scope in scopes.Skip(SecondIndex))
        {
            progress.Record(SharedDomainChildProgressPhase.SeedProducer, operations.Count + SecondIndex);
            var command = SharedDomainCorpus.Producer(scope, CommandId, Item);
            var operation = SharedDomainCrashOperations.Create(database, OperationKind.Batch, command.CommandId, command, time);
            var result = database.Apply(operation);
            _ = result.Get<CommitReceipt>();
            operations.Add(operation);
            results.Add(result);
        }
        var lane = new QueueLaneRef(scopes[SecondIndex].Partition, SecondQueue);
        var receive = new ReceiveRequest(Guid.NewGuid(), lane);
        var receivedOperation = SharedDomainCrashOperations.Create(database, OperationKind.Receive, receive.RequestId, receive, time);
        progress.Record(SharedDomainChildProgressPhase.QueueReceive);
        var received = database.Apply(receivedOperation);
        var delivery = received.Get<ReceiveResult>().Deliveries.Single();
        operations.Add(receivedOperation);
        results.Add(received);
        var acknowledgement = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var acknowledgedOperation = SharedDomainCrashOperations.Create(database, OperationKind.Delivery, acknowledgement.CommandId, acknowledgement, time);
        progress.Record(SharedDomainChildProgressPhase.QueueAcknowledgement);
        var acknowledged = database.Apply(acknowledgedOperation);
        _ = acknowledged.Get<CommitReceipt>();
        operations.Add(acknowledgedOperation);
        results.Add(acknowledged);
        var original = SharedDomainCorpus.Producer(scopes[FirstIndex], CommandId, Item);
        var originalOperation = SharedDomainCrashOperations.Create(database, OperationKind.Batch, original.CommandId, original, time);
        await SaveAsync(directory, store, progress, originalOperation, operations.ToArray(), results.ToArray(), creations);
        progress.Record(SharedDomainChildProgressPhase.ArmingOriginalBoundary);
        boundary.Position = store.Position + FirstRevision;
        boundary.Armed = true;
        _ = database.Apply(originalOperation).Get<CommitReceipt>();
        await RecoveryProcessPause.WaitForKillAsync();
    }

    private static async Task SaveAsync(string directory, ZoneTreeStore store, SharedDomainChildProgressRecorder progress,
        Command<DatabaseCommand> original,
        Command<DatabaseCommand>[] operations, Result<object>[] results, Command<DatabaseCommand>[] creations)
    {
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, SharedDomainCrashProtocol.ResourceCreationsFile, creations);
        progress.Record(SharedDomainChildProgressPhase.SaveOriginalOperation);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, SharedDomainCrashProtocol.OperationFile, original);
        progress.Record(SharedDomainChildProgressPhase.SaveSeedOperations);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, SharedDomainCrashProtocol.SeedOperationsFile, operations);
        progress.Record(SharedDomainChildProgressPhase.SaveSeedResults);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, SharedDomainCrashProtocol.SeedResultsFile, results);
        progress.Record(SharedDomainChildProgressPhase.SaveSeedImage);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, SharedDomainCrashProtocol.SeedImageFile, SharedDomainStoredImage.CaptureRecords(store));
        progress.Record(SharedDomainChildProgressPhase.SaveSeedPosition);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, SharedDomainCrashProtocol.SeedPositionFile, store.Position);
        progress.Record(SharedDomainChildProgressPhase.SaveIdentityDigest);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, SharedDomainCrashProtocol.IdentityDigestFile,
            store.Identity with
            {
                SigningKey = SHA256.HashData(store.Identity.SigningKey.Span)
            });
    }
}
