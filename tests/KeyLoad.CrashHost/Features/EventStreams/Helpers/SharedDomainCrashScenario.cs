using System.Security.Cryptography;
using KeyLoad.CrashHost.Features.DocumentStorage;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.CrashHost.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.CrashHost.Features.EventStreams;

internal static class SharedDomainCrashScenario
{
    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var database = CrashDatabase.Create(store);
        SharedDomainCrashOperations.Configure(database);
        var scopes = SharedDomainCorpus.Create(Tenant, Database);
        var time = TimeProvider.System.GetUtcNow();
        List<ReplicatedOperation> operations = [];
        List<OperationResult> results = [];
        foreach (var scope in scopes.Skip(SecondIndex))
        {
            var command = SharedDomainCorpus.Producer(scope, CommandId, Item);
            var operation = SharedDomainCrashOperations.Create(OperationKind.Batch, command.CommandId, command, time);
            var result = database.Apply(operation);
            _ = result.Get<CommitReceipt>();
            operations.Add(operation);
            results.Add(result);
        }
        var lane = new QueueLaneRef(scopes[SecondIndex].Partition, SecondQueue);
        var receive = new ReceiveRequest(Guid.NewGuid(), lane);
        var receivedOperation = SharedDomainCrashOperations.Create(OperationKind.Receive, receive.RequestId, receive, time);
        var received = database.Apply(receivedOperation);
        var delivery = received.Get<ReceiveResult>().Deliveries.Single();
        operations.Add(receivedOperation);
        results.Add(received);
        var acknowledgement = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var acknowledgedOperation = SharedDomainCrashOperations.Create(OperationKind.Delivery, acknowledgement.CommandId, acknowledgement, time);
        var acknowledged = database.Apply(acknowledgedOperation);
        _ = acknowledged.Get<CommitReceipt>();
        operations.Add(acknowledgedOperation);
        results.Add(acknowledged);
        var original = SharedDomainCorpus.Producer(scopes[FirstIndex], CommandId, Item);
        var originalOperation = SharedDomainCrashOperations.Create(OperationKind.Batch, original.CommandId, original, time);
        await SaveAsync(directory, store, originalOperation, operations.ToArray(), results.ToArray());
        boundary.Position = store.Position + FirstRevision;
        boundary.Armed = true;
        _ = database.Apply(originalOperation).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }

    private static async Task SaveAsync(string directory, ZoneTreeStore store, ReplicatedOperation original,
        ReplicatedOperation[] operations, OperationResult[] results)
    {
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, SharedDomainCrashProtocol.OperationFile, original);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, SharedDomainCrashProtocol.SeedOperationsFile, operations);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, SharedDomainCrashProtocol.SeedResultsFile, results);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, SharedDomainCrashProtocol.SeedImageFile, SharedDomainStoredImage.Capture(store));
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, SharedDomainCrashProtocol.SeedPositionFile, store.Position);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, SharedDomainCrashProtocol.IdentityDigestFile,
            store.Identity with
            {
                SigningKey = SHA256.HashData(store.Identity.SigningKey.Span)
            });
    }
}
