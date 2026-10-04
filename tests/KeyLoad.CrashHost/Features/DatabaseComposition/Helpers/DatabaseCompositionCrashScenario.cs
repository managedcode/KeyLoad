using System.Globalization;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class DatabaseCompositionCrashScenario
{
    internal const string Mode = "database-composition";
    internal const string CommandFile = "composition-command.json";
    internal const string SeedOutboxTailFile = "composition-seed-tail.txt";
    internal const string Principal = "root";
    internal const string Collection = "entities";
    internal const string Graph = "knowledge";
    internal const string SourceQueue = "incoming";
    internal const string TargetQueue = "actions";
    internal const string First = "alice";
    internal const string Second = "bob";
    internal const string SourceMessage = "link-1";
    internal const string EdgePrefix = "edge-";
    internal const string MessagePrefix = "action-";
    internal const string Label = "knows";
    private const string AtomicPartition = "partition";
    internal static PartitionRef Partition { get; } = new("tenant", "database", "domain", AtomicPartition);
    internal static Guid CommandId { get; } = Guid.Parse("1eebc759-9dfa-4785-8f4f-f508934e1a9d");

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var database = CrashDatabase.Create(store);
        Seed(database);
        var tail = database.GetOutboxStatus(Principal, Partition).Head.Tail;
        var request = new CommandRequest(CommandId, Partition,
            [new QueueToGraph(Graph, SourceQueue, EdgePrefix),
                new GraphToQueueMutation(TargetQueue, Graph, Vertex(First), MessagePrefix)]);
        var operation = CrashDatabase.Operation(OperationKind.Batch, request, CommandId);
        await File.WriteAllBytesAsync(Path.Combine(directory, CommandFile), JsonDefaults.Serialize(operation));
        await File.WriteAllTextAsync(Path.Combine(directory, SeedOutboxTailFile),
            tail.ToString(CultureInfo.InvariantCulture));
        boundary.Position = store.Position + 1;
        boundary.Armed = true;
        database.Apply(operation).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }

    private static void Seed(DatabaseEngine database)
    {
        foreach (var (name, kind, id) in new[]
        {
            (Collection, ResourceKind.Collection, Guid.Parse("f5e8bdf6-70b0-4412-9272-350aa90d8d1c")),
            (Graph, ResourceKind.Graph, Guid.Parse("4c26a9a5-a087-45a9-9456-9f6d4ca7a559")),
            (SourceQueue, ResourceKind.WorkQueue, Guid.Parse("6e316398-ac45-4e70-b489-383561931f04")),
            (TargetQueue, ResourceKind.WorkQueue, Guid.Parse("7c1e560d-2106-4910-ae25-73444a6f4189"))
        })
        {
            var resource = new ResourceDefinition(name, kind, Partition.TransactionDomainId);
            CrashDatabase.Submit(database, OperationKind.ConfigureResource,
                new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, resource),
                id).Get<ResourceDefinition>();
        }
        var link = new QueueGraphLink(Vertex(First), Vertex(Second), Label);
        var seedId = Guid.Parse("0f2d5acb-d6af-41e0-8f34-567748432f9f");
        var seed = new CommandRequest(seedId, Partition,
            [new PutDocument(Collection, First, "{}"), new PutDocument(Collection, Second, "{}"),
                new EnqueueMessage(SourceQueue, SourceMessage, JsonSerializer.Serialize(link, JsonDefaults.Options))]);
        CrashDatabase.Submit(database, OperationKind.Batch, seed, seedId).Get<CommitReceipt>();
    }

    private static EntityRef Vertex(string id) => new(Partition, Collection, id);
}
