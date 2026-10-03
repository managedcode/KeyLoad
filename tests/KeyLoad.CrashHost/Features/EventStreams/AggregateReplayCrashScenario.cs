using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class AggregateReplayCrashScenario
{
    internal const string Mode = "aggregate-replay";
    internal const string OperationFile = "aggregate-replay-operation.bin";
    internal const string HeadFile = "aggregate-replay-head.bin";
    internal const string EventFile = "aggregate-replay-event.bin";
    internal const string IdentityFile = "aggregate-replay-identity.bin";
    internal const string StreamSet = "replay-events";
    internal const string StreamId = "aggregate-1";
    internal const string StreamHeadKeySpace = "stream-head";
    internal const string EventKeySpace = "event";
    internal const string EventIdentityKeySpace = "event-id";
    internal const string EventId = "aggregate-event-1";
    internal const string Reducer = "orders.reducer.v1";
    internal const string EventType = "AccountChanged";
    internal const string EventIncrementProperty = "increment";
    internal const string EventPayloadJson = "{\"" + EventIncrementProperty + "\":1}";
    internal const int Generation = 1;
    internal const int StateSchemaVersion = 1;
    internal const int FirstSnapshotVersion = 1;
    internal const long EventRevision = 1;
    internal const int ReplayTailLimit = 1;
    internal const string StateJson = "{\"count\":0}";
    internal static PartitionRef Partition { get; } = new(CrashFixtureValues.Tenant, CrashFixtureValues.Database,
        CrashFixtureValues.Orders, CrashFixtureValues.Partition);
    internal static Guid SnapshotCommandId { get; } = Guid.Parse("cd5b0e77-6dd2-4f1d-8ebf-fb28949d2a5e");

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var database = CrashDatabase.Create(store);
        Seed(database);
        await PreserveSourceBytesAsync(directory, store);
        var command = new StoreAggregateSnapshot(StreamSet, StreamId, 0, Reducer, 1, StateJson, 0, 1);
        var request = new CommandRequest(SnapshotCommandId, Partition, [command]);
        var operation = CrashDatabase.Operation(OperationKind.Batch, request, SnapshotCommandId);
        await File.WriteAllBytesAsync(Path.Combine(directory, OperationFile), NativeSerialization.Serialize(operation));
        boundary.Position = store.Position + 1;
        boundary.Armed = true;
        database.Apply(operation).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }

    private static void Seed(DatabaseEngine database)
    {
        CrashDatabase.Submit(database, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId,
                new(StreamSet, ResourceKind.StreamSet, Partition.TransactionDomainId)), Guid.NewGuid()).Get<ResourceDefinition>();
        var appendId = Guid.Parse("a8ad08f7-0ad4-4e64-944a-b6f68be8592a");
        var append = new CommandRequest(appendId, Partition,
            [new AppendEvents(StreamSet, StreamId, [new(EventId, EventType, EventPayloadJson)],
                ExpectedStreamRevision.NoStream)]);
        CrashDatabase.Submit(database, OperationKind.Batch, append, appendId).Get<CommitReceipt>();
    }

    private static async Task PreserveSourceBytesAsync(string directory, ZoneTreeStore store)
    {
        var headKey = KeySpace.Partition(StreamHeadKeySpace, Partition, StreamSet, StreamId);
        var eventKey = KeySpace.Partition(EventKeySpace, Partition, StreamSet, StreamId, Generation, EventRevision);
        var identityKey = KeySpace.Partition(EventIdentityKeySpace, Partition, StreamSet, StreamId, Generation, EventId);
        var source = store.Read(view => new
        {
            Head = view.ReadOwnedValue(headKey),
            Event = view.ReadOwnedValue(eventKey),
            Identity = view.ReadOwnedValue(identityKey)
        });
        await File.WriteAllBytesAsync(Path.Combine(directory, HeadFile), source.Head!);
        await File.WriteAllBytesAsync(Path.Combine(directory, EventFile), source.Event!);
        await File.WriteAllBytesAsync(Path.Combine(directory, IdentityFile), source.Identity!);
    }
}
