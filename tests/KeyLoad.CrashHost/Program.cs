using KeyLoad;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Replication;
using KeyLoad.Core;
using KeyLoad.Security;
using DotNext.IO.Log;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;

var directory = args[0];
if (args[1] == "raft-append")
{
    await using var log = new DurableRaftLog(new WriteAheadLog.Options
    {
        Location = directory, FlushInterval = Timeout.InfiniteTimeSpan,
        MemoryManagement = WriteAheadLog.MemoryManagementStrategy.PrivateMemory,
        HashAlgorithm = WriteAheadLog.IntegrityHashAlgorithm.Crc64
    }, IStateMachine.CreateNoOp());
    IPersistentState state = log;
    await state.InitializeAsync();
    var entry = new BinaryLogEntry { Content = new byte[] { 1, 2, 3 }, Term = 1 };
    if (args[2] == "indexed") await state.AppendAsync(entry, 1);
    else if (args[2] == "next") await state.AppendAsync(entry);
    else if (args[2] == "batch") await state.AppendAsync(new LogEntryProducer<BinaryLogEntry>([entry]), 1);
    else if (args[2] == "append-and-commit") await state.AppendAndCommitAsync(new LogEntryProducer<BinaryLogEntry>([entry]), 1, false, 1);
    else throw new ArgumentException("Unknown Raft append mode.");
    Console.WriteLine("raft-ack"); Console.Out.Flush();
    Thread.Sleep(Timeout.Infinite);
    return;
}
var stage = Enum.Parse<CommitStage>(args[1]);
var mutationIndex = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
var mode = args.Length > 3 ? args[3] : "commit";
var armed = mode == "commit";
var crashPosition = 2L;
using var store = new ZoneTreeStore(new(directory)
{
    FaultObserver = (observed, position, index) =>
    {
        if (armed && position == crashPosition && observed == stage && (stage != CommitStage.MutationApplied || index == mutationIndex))
        {
            Console.WriteLine("crash-point"); Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite);
        }
    }
});
if (mode == "projection-processing")
{
    var database = new DatabaseEngine(store, new AuthorizationPolicy());
    database.Bootstrap(new("root", "system", [new("*", "*", Capability.All)], ["*"]) { ClusterAdministrator = true },
        DatabaseEngine.Credential("root", "root", "root.crash-test-credential-32-characters"));
    OperationResult Submit<T>(OperationKind kind, T payload, Guid id) => database.Apply(new(id, kind, "root", DateTimeOffset.UtcNow,
        System.Text.Json.JsonSerializer.Serialize(payload, JsonDefaults.Options)));
    var partition = ProjectionCrashScenario.Partition; var consumer = ProjectionCrashScenario.Consumer;
    foreach (var resource in new[] { new ResourceDefinition("orders", ResourceKind.Collection, "orders"), new("projection", ResourceKind.Collection, "orders") })
        Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource), Guid.NewGuid()).Get<ResourceDefinition>();
    var configure = Guid.NewGuid(); Submit(OperationKind.ConfigureProjectionConsumer,
        new ConfigureProjectionConsumerRequest(configure, consumer, new(1, ["orders"], [])), configure).Get<ProjectionConsumerInfo>();
    var producer = Guid.NewGuid(); Submit(OperationKind.Batch,
        new CommandRequest(producer, partition, [new PutDocument("orders", "input", "{}", 0)]), producer).Get<CommitReceipt>();
    var batch = database.ReadProjectionBatch("root", new(consumer));
    var request = new CommitProjectionBatchRequest(ProjectionCrashScenario.CommandId, consumer, batch.Token,
        [new PutDocument("projection", "effect", "{\"complete\":true}", 0)]);
    var operation = new ReplicatedOperation(request.CommandId, OperationKind.CommitProjectionBatch, "root", DateTimeOffset.UtcNow,
        System.Text.Json.JsonSerializer.Serialize(request, JsonDefaults.Options));
    File.WriteAllBytes(Path.Combine(directory, "processing-command.json"), JsonDefaults.Serialize(operation));
    crashPosition = store.Position + 1; armed = true;
    database.Apply(operation).Get<ProjectionBatchResult>();
    Console.WriteLine("ack"); Console.Out.Flush(); Thread.Sleep(Timeout.Infinite); return;
}
if (mode == "subscription-processing")
{
    var database = new DatabaseEngine(store, new AuthorizationPolicy());
    database.Bootstrap(new("root", "system", [new("*", "*", Capability.All)], ["*"]) { ClusterAdministrator = true },
        DatabaseEngine.Credential("root", "root", "root.crash-test-credential-32-characters"));
    OperationResult Submit<T>(OperationKind kind, T payload, Guid id) => database.Apply(new(id, kind, "root", DateTimeOffset.UtcNow,
        System.Text.Json.JsonSerializer.Serialize(payload, JsonDefaults.Options)));
    var partition = SubscriptionCrashScenario.Partition; var subscription = SubscriptionCrashScenario.Subscription;
    foreach (var resource in new[] { new ResourceDefinition("orders", ResourceKind.Collection, "orders"), new("topic", ResourceKind.Topic, "orders") })
        Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource), Guid.NewGuid()).Get<ResourceDefinition>();
    var configure = Guid.NewGuid(); Submit(OperationKind.ConfigureSubscription,
        new ConfigureSubscriptionRequest(configure, subscription, new("root")), configure).Get<SubscriptionInfo>();
    var publish = Guid.NewGuid(); Submit(OperationKind.Batch,
        new CommandRequest(publish, partition, [new PublishTopic("topic", [new("input", "Created", "{}")])]), publish).Get<CommitReceipt>();
    var receive = Guid.NewGuid(); var delivery = Submit(OperationKind.ReceiveSubscription,
        new ReceiveSubscriptionRequest(receive, subscription, LeaseSeconds: 300), receive).Get<ReceiveSubscriptionResult>().Deliveries.Single();
    var request = new SubscriptionProcessingRequest(SubscriptionCrashScenario.CommandId, subscription, delivery.Token, "handler", 1,
        [new PutDocument("orders", "effect", "{\"complete\":true}", 0)]);
    var operation = new ReplicatedOperation(request.CommandId, OperationKind.SubscriptionProcessing, "root", DateTimeOffset.UtcNow,
        System.Text.Json.JsonSerializer.Serialize(request, JsonDefaults.Options));
    File.WriteAllBytes(Path.Combine(directory, "processing-command.json"), JsonDefaults.Serialize(operation));
    crashPosition = store.Position + 1; armed = true;
    database.Apply(operation).Get<SubscriptionProcessingResult>();
    Console.WriteLine("ack"); Console.Out.Flush(); Thread.Sleep(Timeout.Infinite); return;
}
store.Commit((tx, _) => { for (var i = 0; i < 3; i++) tx.PutRecord(KeyCodec.Encode("item", (long)i), 0);
    if (mode == "install") tx.PutRecord(KeyCodec.Encode("obsolete"), true); return true; });
store.Commit((tx, _) => { for (var i = 0; i < 3; i++) tx.PutRecord(KeyCodec.Encode("item", (long)i), 1); return true; });
if (mode == "compact") { armed = true; store.Compact(); }
else if (mode == "install")
{
    var snapshot = Path.Combine(directory, "incoming.snapshot");
    using (var source = new ZoneTreeStore(new(Path.Combine(directory, "snapshot-source"))
        { Incarnation = store.Identity.Incarnation, SigningKey = store.Identity.SigningKey }))
    {
        source.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode("system", "last-applied"), 8L); return true; });
        source.Commit((tx, _) => { for (var i = 0; i < 3; i++) tx.PutRecord(KeyCodec.Encode("item", (long)i), 2);
            tx.PutRecord(KeyCodec.Encode("system", "last-applied"), 9L); return true; });
        source.CreateSnapshot(snapshot, 9);
    }
    armed = true; store.InstallSnapshot(snapshot, 9);
}
Console.WriteLine("ack"); Console.Out.Flush();
Thread.Sleep(Timeout.Infinite);
public sealed class CrashHostMarker;
public static class SubscriptionCrashScenario
{
    public static PartitionRef Partition { get; } = new("tenant", "database", "orders", "partition");
    public static SubscriptionRef Subscription { get; } = new(new(Partition, "topic", EventSourceKind.Topic), "group");
    public static Guid CommandId { get; } = new("cd8b971e-e8ee-4c83-bace-1c30954d097b");
}
public static class ProjectionCrashScenario
{
    public static PartitionRef Partition { get; } = new("tenant", "database", "orders", "partition");
    public static ProjectionConsumerRef Consumer { get; } = new(Partition, "projection-v1");
    public static Guid CommandId { get; } = new("a64f0bcf-93ca-4c6e-a1ca-6f97e1ddbb66");
}
