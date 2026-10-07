using KeyLoad.Core;
using KeyLoad.CrashHost.Features.DocumentStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class EventAppendCrashScenario
{
    private const int PositionStep = 1;
    private const string CollectionCommand = "4d10a82c-bc9a-49d3-b8db-4d0150e325ea";
    private const string StreamCommand = "8184f9f0-2bbf-4b16-9860-f0d509826a53";
    private const string QueueCommand = "81c785fd-22ec-4676-87fa-01f93b9b1f9a";

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var database = CrashDatabase.Create(store);
        Configure(database, EventAppendCrashContract.Documents, ResourceKind.Collection, CollectionCommand);
        Configure(database, EventAppendCrashContract.Streams, ResourceKind.StreamSet, StreamCommand);
        Configure(database, EventAppendCrashContract.Queue, ResourceKind.WorkQueue, QueueCommand);
        var operation = CrashDatabase.Operation(OperationKind.Batch,
            EventAppendCrashContract.ProducerCommand(EventAppendCrashContract.CommandId,
                EventAppendCrashContract.Producer, EventAppendCrashContract.SeedStreamRevision), EventAppendCrashContract.CommandId);
        var seed = new CommandRequest(EventAppendCrashContract.SeedCommandId, EventAppendCrashContract.Partition,
            [new AppendEvents(EventAppendCrashContract.Streams, EventAppendCrashContract.StreamId,
                [EventAppendCrashContract.Event(EventAppendCrashContract.SeedId)], ExpectedStreamRevision.NoStream)]);
        database.Apply(CrashDatabase.Operation(OperationKind.Batch, seed, seed.CommandId)
            with
        { EvaluatedAt = operation.EvaluatedAt }).Get<CommitReceipt>();
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, EventAppendCrashContract.OperationFile, operation);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, EventAppendCrashContract.SeedPositionFile, store.Position);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, EventAppendCrashContract.SeedTailFile,
            database.GetOutboxStatus(EventAppendCrashContract.Principal, EventAppendCrashContract.Partition).Head.Tail);
        boundary.Position = store.Position + PositionStep;
        boundary.Armed = true;
        database.Apply(operation).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }

    private static void Configure(DatabaseEngine database, string name, ResourceKind kind, string id)
        => CrashDatabase.Submit(database, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(EventAppendCrashContract.Partition.TenantId,
                EventAppendCrashContract.Partition.DatabaseId,
                new(name, kind, EventAppendCrashContract.Partition.TransactionDomainId)), Guid.Parse(id))
            .Get<ResourceDefinition>();
}
